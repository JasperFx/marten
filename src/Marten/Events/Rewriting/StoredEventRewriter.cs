using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Rewriting;

/// <summary>
///     Runs one <see cref="StoredEventRewrite" />. Everything it needs is the store and the rewrite: there is no
///     static state and no host, so it can be driven from a console, a test or an agent that keeps the cursor.
/// </summary>
/// <remarks>
///     <see cref="RunAsync" /> is the same rewrite behind <see cref="IEventStore.RewriteStoredEventsAsync" />
///     (jasperfx#1215), where a Wolverine job keeps the cursor in the event-range marks instead.
/// </remarks>
internal sealed class StoredEventRewriter
{
    private static readonly byte[] EmptyJsonObject = "{}"u8.ToArray();

    private readonly DocumentStore _store;
    private readonly StoredEventRewrite _rewrite;
    private readonly EventGraph _events;
    private readonly IEventStorage _storage;
    private readonly ISerializer _serializer;
    private readonly string[]? _typeNames;
    private readonly string _schema;

    public StoredEventRewriter(DocumentStore store, StoredEventRewrite rewrite)
    {
        if (!rewrite.AllTypes && rewrite.Types.Count == 0 && rewrite.TypeNames.Count == 0)
        {
            throw new InvalidOperationException(
                "Name the events to rewrite with IncludeType<T>(), Transform<T>() or IncludeAllTypes()");
        }

        if (rewrite.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(rewrite.BatchSize));
        if (rewrite.MaxParallelDatabases <= 0) throw new ArgumentOutOfRangeException(nameof(rewrite.MaxParallelDatabases));
        if (rewrite.PauseBetweenBatches < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(rewrite.PauseBetweenBatches));

        _store = store;
        _rewrite = rewrite;
        _events = store.Options.EventGraph;
        _storage = (IEventStorage)store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        _serializer = store.Options.Serializer();
        _schema = _events.DatabaseSchemaName;

        if (!rewrite.AllTypes)
        {
            // The type's own name, plus every name an upcaster reads into it
            _typeNames = rewrite.Types
                .Select(t => _events.EventMappingFor(t).EventTypeName)
                .Union(_events.AliasesForEvents(rewrite.Types))
                .Union(rewrite.TypeNames)
                .ToArray();
        }
    }

    public async Task<StoredEventRewriteResult> ExecuteAsync(CancellationToken token)
    {
        var (databases, tenantId) = await selectDatabasesAsync().ConfigureAwait(false);
        var results = new ConcurrentBag<StoredEventRewriteProgress>();

        await Parallel.ForEachAsync(databases,
            new ParallelOptions { MaxDegreeOfParallelism = _rewrite.MaxParallelDatabases, CancellationToken = token },
            async (database, ct) => results.Add(await rewriteAsync(database, tenantId, ct).ConfigureAwait(false)))
            .ConfigureAwait(false);

        return new StoredEventRewriteResult(results.OrderBy(x => x.Cursor.Database).ToList());
    }

    /// <summary>
    ///     jasperfx#1215: one <see cref="EventRewriteRun" /> against one database, a tenant at a time. Each page is
    ///     written in one transaction with its cursor under <see cref="EventRewriteRun.CursorKey" />, so a crash
    ///     never sends an event through a transform twice, and nothing after a tenant's ceiling is read.
    /// </summary>
    public static async Task<IReadOnlyList<EventRewriteProgress>> RunAsync(DocumentStore store, EventRewriteRun run,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(run);

        var events = store.Options.EventGraph;

        // Refuses the whole request before anything is read when a transform is not registered
        var transforms = events.FindRewriteTransforms(run.Definition);

        if (run.Database is not MartenDatabase database || !ReferenceEquals(database.Options, store.Options))
        {
            throw new ArgumentOutOfRangeException(nameof(run),
                $"Database '{run.Database.Identifier}' does not belong to this document store.");
        }

        var rewriter = new StoredEventRewriter(store, rewriteFor(events, run.Definition, transforms));

        var totals = new List<EventRewriteProgress>();
        foreach (var cursor in run.Cursors)
        {
            // Under per-tenant event partitions a sequence repeats across tenants, so a run for the whole
            // database goes through its tenants one after another, each with its own ceiling and cursor
            if (cursor.TenantId == null && events.UseTenantPartitionedEvents)
            {
                foreach (var tenantId in await rewriter.tenantsWithEventsAsync(database, token).ConfigureAwait(false))
                {
                    var tenantCursor = await tenantCursorAsync(database, run, tenantId, token).ConfigureAwait(false);
                    totals.Add(await rewriter.rewriteTenantAsync(database, run, tenantCursor, token)
                        .ConfigureAwait(false));
                }

                continue;
            }

            totals.Add(await rewriter.rewriteTenantAsync(database, run, cursor, token).ConfigureAwait(false));
        }

        return totals;
    }

    private static StoredEventRewrite rewriteFor(EventGraph events, EventRewriteDefinition definition,
        IReadOnlyList<EventRewriteTransform> transforms)
    {
        var rewrite = new StoredEventRewrite
        {
            BatchSize = definition.BatchSize,
            PauseBetweenBatches = definition.PauseBetweenBatches,
            DryRun = definition.DryRun
        };

        if (definition.AllEventTypes) rewrite.IncludeAllTypes();
        if (definition.OnlyEventsStoredAsJson) rewrite.OnlyEventsStoredAsJson();

        // A definition names stored type names. One a registered type answers to brings that type's own name
        // and every other name an upcaster reads into it; one nothing answers to is read as it is, and its
        // events count as unknown.
        foreach (var alias in definition.EventTypes.Distinct())
        {
            var mapping = events.EventMappingFor(alias);
            if (mapping == null)
            {
                rewrite.TypeNames.Add(alias);
            }
            else
            {
                rewrite.Types.Add(mapping.DocumentType);
            }
        }

        // A transform's own type is included without being listed
        foreach (var transform in transforms)
        {
            rewrite.Types.Add(transform.EventType);
            rewrite.Transforms.Add((transform.EventType, transform.Apply));
        }

        return rewrite;
    }

    // The stored ceiling, or the tenant's head inserted if absent, and the stored cursor, as
    // EventRewriteRun.FromMarksAsync builds it for a named tenant. A dry run stores nothing.
    private static async Task<EventRangeCursor> tenantCursorAsync(MartenDatabase database, EventRewriteRun run,
        string tenantId, CancellationToken token)
    {
        if (!run.Definition.DryRun)
        {
            return await EventRangeCursor.FromMarksAsync(database, run.CeilingKey, tenantId,
                t => database.FetchHighestEventSequenceNumber(tenantId, t), token).ConfigureAwait(false);
        }

        var ceiling = await database.FetchEventRangeMarkAsync(run.CeilingKey, tenantId, token).ConfigureAwait(false)
                      ?? await database.FetchHighestEventSequenceNumber(tenantId, token).ConfigureAwait(false);
        var resumeAfter = await database.FetchEventRangeMarkAsync(run.CursorKey, tenantId, token)
            .ConfigureAwait(false) ?? 0;

        return new EventRangeCursor(tenantId, resumeAfter, ceiling);
    }

    private async Task<IReadOnlyList<string>> tenantsWithEventsAsync(MartenDatabase database, CancellationToken token)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            $"select distinct tenant_id from {_schema}.mt_events order by tenant_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

        var tenants = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            tenants.Add(await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false));
        }

        return tenants;
    }

    private async Task<EventRewriteProgress> rewriteTenantAsync(MartenDatabase database, EventRewriteRun run,
        EventRangeCursor cursor, CancellationToken token)
    {
        // With one tenant per database and no conjoined events, the database is the whole tenant
        var tenantFilter = cursor.TenantId != null && _events.TenancyStyle == TenancyStyle.Conjoined
            ? _store.Options.TenantIdStyle.MaybeCorrectTenantId(cursor.TenantId)
            : null;

        var total = new EventRewriteProgress(cursor.TenantId, cursor.ResumeAfter, 0, 0, 0, 0, 0, 0, cursor.IsComplete);
        var position = cursor.ResumeAfter;

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        while (!total.Completed)
        {
            token.ThrowIfCancellationRequested();

            // Sequences are unique within one tenant's events, and store-wide without per-tenant partitions
            var page = await readPageAsync(conn, new StoredEventRewriteCursor(database.Identifier, position, ""),
                tenantFilter, cursor.StopAfter, bySequenceOnly: true, token).ConfigureAwait(false);

            // The last page stores the ceiling as the cursor, so the run reads as complete when it is built again
            var completed = !page.More;
            position = completed ? cursor.StopAfter : page.LastSequence;

            var written = (Rewritten: 0L, Skipped: 0L, BytesBefore: 0L, BytesAfter: 0L);
            if (_rewrite.DryRun)
            {
                // Nothing is written, not even the cursor
                if (page.Events.Count > 0)
                {
                    written = await writePageAsync(conn, null, page.Events, token).ConfigureAwait(false);
                }
            }
            else
            {
                // The page and its cursor commit together or not at all
                await using var tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false);
                if (page.Events.Count > 0)
                {
                    written = await writePageAsync(conn, tx, page.Events, token).ConfigureAwait(false);
                }

                await database.SetEventRangeMarkAsync(conn, tx, run.CursorKey, cursor.TenantId, position, token)
                    .ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
            }

            var progress = new EventRewriteProgress(cursor.TenantId, position, page.Rows, written.Rewritten,
                written.Skipped, written.BytesBefore, written.BytesAfter, page.Rows - page.Events.Count, completed);

            total = total with
            {
                Sequence = position,
                Visited = total.Visited + progress.Visited,
                Rewritten = total.Rewritten + progress.Rewritten,
                Skipped = total.Skipped + progress.Skipped,
                BytesBefore = total.BytesBefore + progress.BytesBefore,
                BytesAfter = total.BytesAfter + progress.BytesAfter,
                UnknownTypes = total.UnknownTypes + progress.UnknownTypes,
                Completed = completed
            };

            if (run.OnPage != null)
            {
                await run.OnPage(progress, token).ConfigureAwait(false);
            }

            if (!completed && _rewrite.PauseBetweenBatches > TimeSpan.Zero)
            {
                await Task.Delay(_rewrite.PauseBetweenBatches, token).ConfigureAwait(false);
            }
        }

        return total;
    }

    private async Task<(IReadOnlyList<IMartenDatabase> Databases, string? TenantId)> selectDatabasesAsync()
    {
        IMartenDatabase? tenantDatabase = null;
        string? tenantId = null;
        if (_rewrite.TenantId != null)
        {
            var tenant = await _store.Tenancy
                .GetTenantAsync(_store.Options.TenantIdStyle.MaybeCorrectTenantId(_rewrite.TenantId))
                .ConfigureAwait(false);
            tenantDatabase = tenant.Database;

            // With one tenant per database and no conjoined events, the database is the whole selection
            if (_events.TenancyStyle == TenancyStyle.Conjoined) tenantId = tenant.TenantId;
        }

        if (_rewrite.DatabaseIdentifier == null)
        {
            return tenantDatabase == null
                ? (await _store.Storage.AllDatabases().ConfigureAwait(false), tenantId)
                : ([tenantDatabase], tenantId);
        }

        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        var database = databases.FirstOrDefault(x => x.Identifier == _rewrite.DatabaseIdentifier)
                       ?? throw new ArgumentOutOfRangeException(nameof(StoredEventRewrite.ForDatabase),
                           $"Unknown database '{_rewrite.DatabaseIdentifier}'. Known: {databases.Select(x => x.Identifier).Join(", ")}");

        if (tenantDatabase != null && tenantDatabase.Identifier != database.Identifier)
        {
            throw new InvalidOperationException(
                $"Tenant '{_rewrite.TenantId}' is stored in database '{tenantDatabase.Identifier}', not '{database.Identifier}'");
        }

        return ([database], tenantId);
    }

    private async Task<StoredEventRewriteProgress> rewriteAsync(IMartenDatabase database, string? tenantId,
        CancellationToken token)
    {
        var cursor = _rewrite.ResumeCursors.TryGetValue(database.Identifier, out var resume)
            ? resume
            : new StoredEventRewriteCursor(database.Identifier, 0, "");
        var stopAfter = _rewrite.StopAfterFor(database.Identifier);

        long visited = 0, rewritten = 0, skipped = 0, before = 0, after = 0, unknown = 0;

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var page = await readPageAsync(conn, cursor, tenantId, stopAfter, bySequenceOnly: false, token)
                .ConfigureAwait(false);
            if (page.Rows > 0)
            {
                if (page.Events.Count > 0)
                {
                    var written = await writePageAsync(conn, null, page.Events, token).ConfigureAwait(false);
                    rewritten += written.Rewritten;
                    skipped += written.Skipped;
                    before += written.BytesBefore;
                    after += written.BytesAfter;
                }

                visited += page.Rows;
                unknown += page.Rows - page.Events.Count;
                cursor = cursor with { Sequence = page.LastSequence, TenantId = page.LastTenantId };
            }

            var completed = page.Rows < _rewrite.BatchSize;
            var progress = new StoredEventRewriteProgress(cursor, visited, rewritten, skipped, before, after,
                completed, unknown);
            if (_rewrite.OnProgress != null)
            {
                await _rewrite.OnProgress(progress, token).ConfigureAwait(false);
            }

            if (completed) return progress;

            if (_rewrite.PauseBetweenBatches > TimeSpan.Zero)
            {
                await Task.Delay(_rewrite.PauseBetweenBatches, token).ConfigureAwait(false);
            }
        }
    }

    /// <param name="More">Whether another selected row follows this page, up to the ceiling.</param>
    private sealed record Page(List<(IEvent Event, string Xmin)> Events, int Rows, long LastSequence,
        string LastTenantId, bool More);

    /// <param name="bySequenceOnly">
    ///     Page by <c>seq_id</c> alone, for one tenant or a store whose sequence is store-wide. Otherwise by
    ///     <c>(seq_id, tenant_id)</c>, because a sequence repeats across per-tenant event partitions.
    /// </param>
    private async Task<Page> readPageAsync(NpgsqlConnection conn, StoredEventRewriteCursor cursor, string? tenantId,
        long? stopAfter, bool bySequenceOnly, CancellationToken token)
    {
        var fields = _storage.SelectFields();

        var filters = new List<string>();
        if (_typeNames != null) filters.Add("d.type = any(@types)");
        if (tenantId != null) filters.Add("d.tenant_id = @tenant_id");
        if (_rewrite.JsonOnly) filters.Add("d.bdata is null");
        if (stopAfter.HasValue) filters.Add("d.seq_id <= @stop");
        filters.Add(bySequenceOnly ? "d.seq_id > @seq" : "(d.seq_id, d.tenant_id) > (@seq, @tenant)");

        var order = bySequenceOnly ? "d.seq_id" : "d.seq_id, d.tenant_id";

        // seq_id and tenant_id are read again after the event's own fields, so the cursor moves past a row
        // even when the event in it cannot be read. One row more than the page is asked for and not read,
        // so the last page is known to be the last without reading an empty one after it.
        await using var cmd = new NpgsqlCommand(
            $"select {fields.Select(x => "d." + x).Join(", ")}, d.xmin::text, d.seq_id, d.tenant_id " +
            $"from {_schema}.mt_events as d where {filters.Join(" and ")} order by {order} limit @limit",
            conn);

        if (_typeNames != null) cmd.Parameters.AddWithValue("types", NpgsqlDbType.Array | NpgsqlDbType.Varchar, _typeNames);
        if (tenantId != null) cmd.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, tenantId);
        if (stopAfter.HasValue) cmd.Parameters.AddWithValue("stop", NpgsqlDbType.Bigint, stopAfter.Value);
        cmd.Parameters.AddWithValue("seq", NpgsqlDbType.Bigint, cursor.Sequence);
        if (!bySequenceOnly) cmd.Parameters.AddWithValue("tenant", NpgsqlDbType.Varchar, cursor.TenantId);
        cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, _rewrite.BatchSize + 1);

        var events = new List<(IEvent, string)>(_rewrite.BatchSize);
        var rows = 0;
        long lastSequence = cursor.Sequence;
        var lastTenantId = cursor.TenantId;
        var more = false;

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (rows == _rewrite.BatchSize)
            {
                more = true;
                break;
            }

            rows++;
            var xmin = await reader.GetFieldValueAsync<string>(fields.Length, token).ConfigureAwait(false);
            lastSequence = await reader.GetFieldValueAsync<long>(fields.Length + 1, token).ConfigureAwait(false);
            lastTenantId = await reader.GetFieldValueAsync<string>(fields.Length + 2, token).ConfigureAwait(false);

            IEvent @event;
            try
            {
                @event = await ((ISelector<IEvent>)_storage).ResolveAsync(reader, token).ConfigureAwait(false);
            }
            catch (UnknownEventTypeException)
            {
                // Only reachable with IncludeAllTypes or a stored type name nothing answers to. An event the
                // application cannot read cannot be written back the way it would be stored today, so it stays
                // as it is.
                continue;
            }

            events.Add((@event, xmin));
        }

        return new Page(events, rows, lastSequence, lastTenantId, more);
    }

    private async Task<(long Rewritten, long Skipped, long BytesBefore, long BytesAfter)> writePageAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, List<(IEvent Event, string Xmin)> page, CancellationToken token)
    {
        var count = page.Count;
        var seqs = new long[count];
        var tenants = new string[count];
        var xmins = new string[count];
        var types = new string[count];
        var dotNetTypes = new string[count];
        var data = new byte[count][];
        var bdata = new byte[]?[count];

        for (var i = 0; i < count; i++)
        {
            var (e, xmin) = page[i];
            var body = transform(e.Data);
            var mapping = _events.EventMappingFor(body.GetType());

            seqs[i] = e.Sequence;
            tenants[i] = e.TenantId;
            xmins[i] = xmin;
            types[i] = mapping.EventTypeName;
            dotNetTypes[i] = mapping.DotNetTypeName;

            // The same split an append makes: a binary event keeps {} in data and its bytes in bdata
            if (mapping.IsBinary)
            {
                data[i] = EmptyJsonObject;
                bdata[i] = mapping.BinarySerializer!.Serialize(mapping.DocumentType, body);
            }
            else
            {
                using var buffer = new PooledByteBufferWriter();
                _serializer.WriteTo(buffer, body);
                data[i] = buffer.ToSizedArray();
            }
        }

        // Rows that would come out the same are not written, so they leave no dead tuple behind. The xmin
        // check is repeated on the update itself, so a row another transaction changed after the read is
        // skipped rather than overwritten. Under READ COMMITTED an update that waits on a concurrent writer
        // re-checks its WHERE against the new row version, whose xmin no longer matches.
        var write = _rewrite.DryRun
            ? "select 0 where false"
            : $"""
               update {_schema}.mt_events as e
               set type = c.type, mt_dotnet_type = c.dotnet, data = c.data, bdata = c.bdata
               from changed as c
               where e.seq_id = c.seq_id and e.tenant_id = c.tenant_id and e.xmin::text = c.xmin
               returning 1
               """;

        await using var cmd = new NpgsqlCommand(
            $"""
             with u as (
               select * from unnest(@seqs, @tenants, @xmins, @types, @dotnets, @data, @bdata)
                 as u(seq_id, tenant_id, xmin, type, dotnet, data, bdata)
             ), changed as (
               select u.*, e.xmin::text = u.xmin as unchanged_since_read,
                      pg_column_size(e.data) + coalesce(pg_column_size(e.bdata), 0) as bytes_before,
                      pg_column_size(u.data) + coalesce(pg_column_size(u.bdata), 0) as bytes_after
               from u join {_schema}.mt_events as e on e.seq_id = u.seq_id and e.tenant_id = u.tenant_id
               where e.type is distinct from u.type or e.mt_dotnet_type is distinct from u.dotnet
                  or e.data is distinct from u.data or e.bdata is distinct from u.bdata
             ), written as ({write})
             select (select count(*) from {(_rewrite.DryRun ? "changed where unchanged_since_read" : "written")}),
                    (select count(*) from changed),
                    (select coalesce(sum(bytes_before), 0) from changed where unchanged_since_read),
                    (select coalesce(sum(bytes_after), 0) from changed where unchanged_since_read)
             """, conn, tx);

        cmd.Parameters.AddWithValue("seqs", NpgsqlDbType.Array | NpgsqlDbType.Bigint, seqs);
        cmd.Parameters.AddWithValue("tenants", NpgsqlDbType.Array | NpgsqlDbType.Varchar, tenants);
        cmd.Parameters.AddWithValue("xmins", NpgsqlDbType.Array | NpgsqlDbType.Text, xmins);
        cmd.Parameters.AddWithValue("types", NpgsqlDbType.Array | NpgsqlDbType.Varchar, types);
        cmd.Parameters.AddWithValue("dotnets", NpgsqlDbType.Array | NpgsqlDbType.Varchar, dotNetTypes);
        cmd.Parameters.AddWithValue("data", NpgsqlDbType.Array | NpgsqlDbType.Jsonb, data);
        cmd.Parameters.AddWithValue("bdata", NpgsqlDbType.Array | NpgsqlDbType.Bytea, bdata);

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        await reader.ReadAsync(token).ConfigureAwait(false);

        var rewritten = reader.GetInt64(0);
        var changed = reader.GetInt64(1);
        return (rewritten, changed - rewritten, reader.GetInt64(2), reader.GetInt64(3));
    }

    private object transform(object data)
    {
        foreach (var (type, transform) in _rewrite.Transforms)
        {
            if (!type.IsInstanceOfType(data)) continue;

            data = transform(data) ?? throw new InvalidOperationException(
                $"The transform for {type.FullName} returned null. Return the event to keep it as it is.");
        }

        return data;
    }
}
