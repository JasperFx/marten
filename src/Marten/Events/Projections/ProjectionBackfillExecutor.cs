using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Events.Schema;
using Marten.Internal.Sessions;
using Marten.Patching;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Projections;

/// <summary>
///     The backfills one aggregation projection declares, and how its shard runs them.
/// </summary>
internal class ProjectionBackfills<TDoc, TId> where TDoc : notnull where TId : notnull
{
    private readonly List<ProjectionBackfill<TDoc, TId>> _backfills = new();

    public IReadOnlyList<ProjectionBackfill<TDoc, TId>> All => _backfills;

    public ProjectionBackfill<TDoc, TId> Add(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentOutOfRangeException(nameof(name), "A backfill needs a name");
        }

        if (_backfills.Any(x => x.Name == name))
        {
            throw new ArgumentOutOfRangeException(nameof(name), $"There is already a backfill named '{name}'");
        }

        var backfill = new ProjectionBackfill<TDoc, TId>(name);
        _backfills.Add(backfill);
        return backfill;
    }

    public IEnumerable<string> Validate(ProjectionBase projection)
    {
        if (_backfills.Count > 0 && projection.Lifecycle == ProjectionLifecycle.Inline)
        {
            yield return $"Projection {projection.Name} declares backfills, which only an async projection runs";
        }

        foreach (var backfill in _backfills.Where(x => x.EventTypes.Count == 0))
        {
            yield return $"Backfill '{backfill.Name}' on projection {projection.Name} handles no event types";
        }
    }

    public bool TryBuildExecutor(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        ShardName shardName, ILogger logger, JasperFxAggregationProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession> projection,
        [NotNullWhen(true)] out IBackfillExecutor? executor)
    {
        executor = _backfills.Count == 0
            ? null
            : new ProjectionBackfillExecutor<TDoc, TId>((DocumentStore)store, (IMartenDatabase)database, shardName,
                projection, _backfills, logger);

        return executor != null;
    }
}

internal class ProjectionBackfillExecutor<TDoc, TId>: IBackfillExecutor where TDoc : notnull where TId : notnull
{
    private const int MaxAttemptsPerPage = 5;

    private readonly IReadOnlyList<ProjectionBackfill<TDoc, TId>> _backfills;
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private readonly JasperFxAggregationProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession> _projection;
    private readonly ShardName _shardName;
    private readonly DocumentStore _store;
    private readonly string _schema;
    private readonly string _stateTable;
    private readonly string _valuesTable;
    private readonly string _documentTable;
    private readonly DocumentMapping _mapping;
    private readonly HashSet<Type> _deleteTypes;

    // How far each pending backfill got in CatchUpAsync, for HandOverAsync
    private readonly Dictionary<string, long> _positions = new();

    public ProjectionBackfillExecutor(DocumentStore store, IMartenDatabase database, ShardName shardName,
        JasperFxAggregationProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession> projection,
        IReadOnlyList<ProjectionBackfill<TDoc, TId>> backfills, ILogger logger)
    {
        _store = store;
        _database = database;
        _shardName = shardName;
        _projection = projection;
        _backfills = backfills;
        _logger = logger;
        _schema = store.Options.DatabaseSchemaName;
        _stateTable = $"{store.Options.Events.DatabaseSchemaName}.{ProjectionBackfillTable.Name}";
        _mapping = (DocumentMapping)store.Options.Storage.FindMapping(typeof(TDoc)).Root;
        _documentTable = _mapping.TableName.QualifiedName;
        _valuesTable = ProjectionBackfillValuesTable.NameFor(_mapping).QualifiedName;
        _deleteTypes = projection.DeleteEventTypes.ToHashSet();
    }

    private string nameOf(ProjectionBackfill<TDoc, TId> backfill) => $"{_shardName.Identity}:{backfill.Name}";

    // The backfill writes the document table in plain SQL, so make sure it has its current shape first
    private ValueTask ensureStorageAsync(CancellationToken token) =>
        _database.EnsureStorageExistsAsync(typeof(TDoc), token);

    public async Task SettleAsync(CancellationToken token)
    {
        await ensureStorageAsync(token).ConfigureAwait(false);

        await using var session = openSession();
        foreach (var backfill in _backfills)
        {
            var name = nameOf(backfill);
            session.QueueSqlCommand($"delete from {_valuesTable} where name = ?", name);
            session.QueueSqlCommand(
                $"insert into {_stateTable} (name, position, ceiling, completed) values (?, 0, 0, transaction_timestamp()) on conflict (name) do update set completed = coalesce({_stateTable}.completed, excluded.completed), last_updated = transaction_timestamp()",
                name);
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async Task<bool> CatchUpAsync(Func<long> liveProgression, BackfillRequest request, CancellationToken token)
    {
        await ensureStorageAsync(token).ConfigureAwait(false);

        foreach (var backfill in _backfills)
        {
            var name = nameOf(backfill);
            var (position, completed) = await readStateAsync(name, token).ConfigureAwait(false);
            if (completed)
            {
                continue;
            }

            _logger.LogInformation("Backfilling {Backfill} from {Floor} alongside the live projection at {Live}",
                name, position, liveProgression());
            await request.ReportAsync(new BackfillProgress(name, position, liveProgression())).ConfigureAwait(false);

            var loader = buildLoader(backfill, request.BatchSize);
            long live;
            while ((live = liveProgression()) - position > request.BatchSize)
            {
                position = await foldPageAsync(backfill, loader, position, live, request, handOver: false, token)
                    .ConfigureAwait(false);
            }

            _positions[name] = position;
        }

        return _positions.Count > 0;
    }

    public async Task HandOverAsync(long ceiling, BackfillRequest request, CancellationToken token)
    {
        foreach (var backfill in _backfills)
        {
            var name = nameOf(backfill);
            if (!_positions.TryGetValue(name, out var position))
            {
                continue;
            }

            var loader = buildLoader(backfill, request.BatchSize);
            do
            {
                position = await foldPageAsync(backfill, loader, position, ceiling, request, handOver: true, token)
                    .ConfigureAwait(false);
            } while (position < ceiling);

            _logger.LogInformation("Backfill {Backfill} is handed over at {Ceiling}", name, ceiling);
        }

        _positions.Clear();
    }

    private EventLoader buildLoader(ProjectionBackfill<TDoc, TId> backfill, int batchSize)
    {
        var filtering = new EventFilterable
        {
            StreamType = _projection.StreamType, IncludeArchivedEvents = _projection.IncludeArchivedEvents
        };
        filtering.IncludedEventTypes.AddRange(backfill.EventTypes.Concat(_deleteTypes).Distinct());

        return new EventLoader(_store, (MartenDatabase)_database, new AsyncOptions { BatchSize = batchSize },
            _store.buildEventLoaderFilters(filtering).ToArray(), filtering.IncludeArchivedEvents, _shardName);
    }

    // Folds one page into the side table and merges what changed into the documents, in one transaction with
    // the backfill's position. The last page of a hand-over completes the backfill. Returns the new position.
    private async Task<long> foldPageAsync(ProjectionBackfill<TDoc, TId> backfill, EventLoader loader, long floor,
        long ceiling, BackfillRequest request, bool handOver, CancellationToken token)
    {
        var name = nameOf(backfill);
        var page = await loader.LoadAsync(new EventRequest
        {
            Floor = floor,
            HighWater = ceiling,
            BatchSize = request.BatchSize,
            Name = _shardName,
            ErrorOptions = request.ErrorHandling,
            Runtime = request.Runtime
        }, token).ConfigureAwait(false);

        var complete = handOver && page.Ceiling >= ceiling;

        for (var attempt = 1;; attempt++)
        {
            await using var session = openSession();
            if (page.Count > 0)
            {
                await foldAsync(session, backfill, name, page).ConfigureAwait(false);
            }

            queueMerge(session, name, complete);

            if (complete)
            {
                session.QueueSqlCommand($"delete from {_valuesTable} where name = ?", name);
                session.QueueSqlCommand(
                    $"insert into {_stateTable} (name, position, ceiling, completed) values (?, ?, ?, transaction_timestamp()) on conflict (name) do update set position = excluded.position, ceiling = excluded.ceiling, completed = excluded.completed, last_updated = transaction_timestamp()",
                    name, page.Ceiling, ceiling);
            }
            else
            {
                session.QueueSqlCommand(
                    $"insert into {_stateTable} (name, position, ceiling) values (?, ?, ?) on conflict (name) do update set position = excluded.position, ceiling = excluded.ceiling, last_updated = transaction_timestamp()",
                    name, page.Ceiling, ceiling);
            }

            try
            {
                await session.SaveChangesAsync(token).ConfigureAwait(false);
                await request.ReportAsync(new BackfillProgress(name, page.Ceiling, ceiling) { Completed = complete })
                    .ConfigureAwait(false);
                return page.Ceiling;
            }
            catch (Exception e) when (attempt < MaxAttemptsPerPage && isConflictWithTheLiveProjection(e))
            {
                // The merge and a live batch locked the same documents in a different order
                _logger.LogDebug(e, "Retrying backfill page {Floor}-{Ceiling} of {Backfill}", floor, page.Ceiling, name);
            }
        }
    }

    private static bool isConflictWithTheLiveProjection(Exception e)
    {
        var postgres = e as PostgresException ?? e.InnerException as PostgresException;
        return postgres?.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure;
    }

    private async Task foldAsync(DocumentSessionBase session, ProjectionBackfill<TDoc, TId> backfill, string name,
        IReadOnlyList<IEvent> events)
    {
        // The projection's own slicer, so a backfill finds exactly the documents the projection wrote
        var slicer = _projection.BuildSlicer(session);
        var storage = session.StorageFor<TDoc>();

        foreach (var group in (await slicer.SliceAsync(events).ConfigureAwait(false)).OfType<SliceGroup<TDoc, TId>>())
        {
            foreach (var slice in group.Slices)
            {
                var rawId = storage.RawIdentityValue(slice.Id!);
                var patch = new PatchExpression<TDoc>(session);

                foreach (var e in slice.Events())
                {
                    if (_deleteTypes.Contains(e.EventType))
                    {
                        // The document starts over after a delete, and so does its backfilled member
                        session.QueueSqlCommand(
                            $"delete from {_valuesTable} where name = ? and tenant_id = ? and id = ?",
                            name, group.TenantId, rawId);
                        patch = new PatchExpression<TDoc>(session);
                        continue;
                    }

                    backfill.Apply(patch, e);
                }

                queueFold(session, name, group.TenantId, rawId, patch);
            }
        }
    }

    private void queueFold(DocumentSessionBase session, string name, string tenantId, object rawId,
        PatchExpression<TDoc> patch)
    {
        if (patch.PatchSet.Count == 0)
        {
            return;
        }

        string patchSet;
        using (var buffer = new PooledByteBufferWriter())
        {
            new PatchFragment(session, patch.PatchSet, session.Serializer,
                    new PostgresqlObjectName(_schema, "mt_jsonb_patch"), session.StorageFor<TDoc>())
                .writePatchArray(buffer);
            patchSet = Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        // A nested Set needs its parent objects, which the empty starting point does not have
        var paths = patch.PatchSet
            .Select(x => x.Items.TryGetValue("path", out var path) ? path?.ToString() : null)
            .Where(x => x != null && x.Contains('.'))
            .Distinct()
            .ToArray();

        var parameters = new List<object> { name, tenantId, rawId };

        string patched(string json)
        {
            foreach (var path in paths)
            {
                json = $"{_schema}.mt_jsonb_fix_null_parent({json}, {_schema}.mt_jsonb_path_to_array(?, '\\.'))";
                parameters.Add(path!);
            }

            parameters.Add(patchSet);
            return $"{_schema}.mt_jsonb_patch({json}, ?::jsonb)";
        }

        var inserted = patched("'{}'::jsonb");
        var updated = patched("s.data");

        session.QueueSqlCommand(
            $"insert into {_valuesTable} as s (name, tenant_id, id, data, dirty) values (?, ?, ?, {inserted}, true) on conflict (name, tenant_id, id) do update set data = {updated}, dirty = true",
            parameters.ToArray());
    }

    // Writes the backfilled members into the documents that still exist and are not soft-deleted. Alongside
    // the live projection this merges only what the backfill changed; the hand-over also re-merges every
    // document the live projection has rewritten since its last merge. Each statement finds its rows through
    // the partial index on dirty, so a page costs what it changed, not the size of the backfill.
    private void queueMerge(DocumentSessionBase session, string name, bool handOver)
    {
        var tenant = _mapping.TenancyStyle == TenancyStyle.Conjoined ? " and d.tenant_id = s.tenant_id" : "";
        var notDeleted = _mapping.DeleteStyle == DeleteStyle.SoftDelete ? " and d.mt_deleted = false" : "";

        if (handOver)
        {
            // The one full pass: documents the live projection rewrote since their merge are merged again
            session.QueueSqlCommand(
                $"update {_valuesTable} as s set dirty = true from {_documentTable} as d where s.name = ? and not s.dirty and d.id = s.id{tenant} and (s.merged_xmin is null or not (d.xmin = s.merged_xmin))",
                name);
        }

        session.QueueSqlCommand(
            $"update {_documentTable} as d set data = {_schema}.mt_jsonb_deep_merge(d.data, s.data) from {_valuesTable} as s where s.name = ? and s.dirty and d.id = s.id{tenant}{notDeleted}",
            name);

        if (_mapping.DuplicatedFields.Count > 0)
        {
            // Duplicated columns are computed from data, so only after it changed. The subquery keeps "data" unambiguous
            var columns = string.Join(", ", _mapping.DuplicatedFields.Select(x => x.UpdateSqlFragment()));
            session.QueueSqlCommand(
                $"update {_documentTable} as d set {columns} from (select id, tenant_id from {_valuesTable} where name = ? and dirty) as s where d.id = s.id{tenant} and d.xmin = pg_current_xact_id()::xid",
                name);
        }

        // A deleted document has no version to record, and is not merged again by the hand-over
        session.QueueSqlCommand(
            $"update {_valuesTable} as s set dirty = false, merged_xmin = (select d.xmin from {_documentTable} as d where d.id = s.id{tenant}) where s.name = ? and s.dirty",
            name);
    }

    private DocumentSessionBase openSession() =>
        (DocumentSessionBase)_store.LightweightSession(SessionOptions.ForDatabase(_database));

    private async Task<(long Position, bool Completed)> readStateAsync(string name, CancellationToken token)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand($"select position, completed is not null from {_stateTable} where name = $1", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = name });

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        return await reader.ReadAsync(token).ConfigureAwait(false)
            ? (reader.GetInt64(0), reader.GetBoolean(1))
            : (0, false);
    }
}
