using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Rewriting;

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
        if (!rewrite.AllTypes && rewrite.Types.Count == 0)
        {
            throw new InvalidOperationException(
                "Name the events to rewrite with IncludeType<T>(), Transform<T>() or IncludeAllTypes()");
        }

        if (rewrite.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(rewrite.BatchSize));
        if (rewrite.MaxParallelDatabases <= 0) throw new ArgumentOutOfRangeException(nameof(rewrite.MaxParallelDatabases));

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
                .ToArray();
        }
    }

    public async Task<StoredEventRewriteResult> ExecuteAsync(CancellationToken token)
    {
        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        var results = new ConcurrentBag<StoredEventRewriteProgress>();

        await Parallel.ForEachAsync(databases,
            new ParallelOptions { MaxDegreeOfParallelism = _rewrite.MaxParallelDatabases, CancellationToken = token },
            async (database, ct) => results.Add(await rewriteAsync(database, ct).ConfigureAwait(false))).ConfigureAwait(false);

        return new StoredEventRewriteResult(results.OrderBy(x => x.Cursor.Database).ToList());
    }

    private async Task<StoredEventRewriteProgress> rewriteAsync(IMartenDatabase database, CancellationToken token)
    {
        var cursor = _rewrite.ResumeCursors.TryGetValue(database.Identifier, out var resume)
            ? resume
            : new StoredEventRewriteCursor(database.Identifier, 0, "");

        long visited = 0, rewritten = 0, skipped = 0, before = 0, after = 0;

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        while (true)
        {
            var page = await readPageAsync(conn, cursor, token).ConfigureAwait(false);
            if (page.Count > 0)
            {
                var written = await writePageAsync(conn, page, token).ConfigureAwait(false);
                visited += page.Count;
                rewritten += written.Rewritten;
                skipped += written.Skipped;
                before += written.BytesBefore;
                after += written.BytesAfter;

                var last = page[^1].Event;
                cursor = cursor with { Sequence = last.Sequence, TenantId = last.TenantId };
            }

            var completed = page.Count < _rewrite.BatchSize;
            var progress = new StoredEventRewriteProgress(cursor, visited, rewritten, skipped, before, after, completed);
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

    private async Task<List<(IEvent Event, string Xmin)>> readPageAsync(NpgsqlConnection conn,
        StoredEventRewriteCursor cursor, CancellationToken token)
    {
        var fields = _storage.SelectFields();
        var typeFilter = _typeNames == null ? "" : "d.type = any(@types) and ";

        await using var cmd = new NpgsqlCommand(
            $"select {fields.Select(x => "d." + x).Join(", ")}, d.xmin::text from {_schema}.mt_events as d " +
            $"where {typeFilter}(d.seq_id, d.tenant_id) > (@seq, @tenant) order by d.seq_id, d.tenant_id limit @limit",
            conn);

        if (_typeNames != null) cmd.Parameters.AddWithValue("types", NpgsqlDbType.Array | NpgsqlDbType.Varchar, _typeNames);
        cmd.Parameters.AddWithValue("seq", NpgsqlDbType.Bigint, cursor.Sequence);
        cmd.Parameters.AddWithValue("tenant", NpgsqlDbType.Varchar, cursor.TenantId);
        cmd.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, _rewrite.BatchSize);

        var page = new List<(IEvent, string)>(_rewrite.BatchSize);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var @event = await ((ISelector<IEvent>)_storage).ResolveAsync(reader, token).ConfigureAwait(false);
            page.Add((@event, await reader.GetFieldValueAsync<string>(fields.Length, token).ConfigureAwait(false)));
        }

        return page;
    }

    private async Task<(long Rewritten, long Skipped, long BytesBefore, long BytesAfter)> writePageAsync(
        NpgsqlConnection conn, List<(IEvent Event, string Xmin)> page, CancellationToken token)
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
        // skipped rather than overwritten.
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
             """, conn);

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
            if (type.IsInstanceOfType(data)) data = transform(data);
        }

        return data;
    }
}
