#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Behind <c>MarkStreamCold</c> and <c>MarkStreamsCold</c>: deletes each stream's events through its
/// <c>throughVersion</c> from <c>mt_events</c> (and their DCB tag rows), and records the boundary in
/// <c>mt_streams.cold_version</c>.
/// </summary>
/// <remarks>
/// <para>
/// One statement of data-modifying CTEs over the whole batch, for the reasons on
/// <see cref="DeleteStreamOperation" />. The boundary only ever moves up. The statement always returns one row,
/// so the unit of work fails when a stream does not exist or has not reached its <c>throughVersion</c>, or when
/// an async shard has not processed the events yet.
/// </para>
/// <para>
/// The lag guard compares every registered async shard's <c>mt_event_progression</c> row with the highest
/// <c>seq_id</c> about to be deleted in the whole batch, and deletes nothing for any stream when one is behind
/// or has no row. Row names are composed exactly as the daemon composes them: per tenant
/// (<c>{shard}:{tenant}</c>) when the store distributes agents per tenant, which is
/// <c>UseTenantPartitionedEvents</c>, and store-global otherwise, in which case <c>seq_id</c> is store-global
/// too. A shard that is merely between batches has not committed progress past those events, so it is counted
/// as behind -- the guard never lets a delete overtake a shard.
/// </para>
/// </remarks>
internal class MarkStreamColdOperation: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly Dictionary<string, long> _throughVersions = new();
    private readonly string _tenantId;

    public MarkStreamColdOperation(EventGraph events, string tenantId)
    {
        _events = events;
        _tenantId = tenantId;
    }

    internal string TenantId => _tenantId;

    internal int Count => _throughVersions.Count;

    /// <summary>
    /// Adds streams to the batch. A stream named twice moves as far as its highest version, as two separate
    /// statements would. Nothing is added when one of them is invalid.
    /// </summary>
    internal void Add(IReadOnlyList<(string Key, long Version)> streams)
    {
        foreach (var (key, version) in streams)
        {
            if (key == null) throw new ArgumentException("A stream key is null.", nameof(streams));
            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(streams),
                    $"A stream's cold part has at least version 1, not {version} for '{key}'.");
            }
        }

        foreach (var (key, version) in streams)
        {
            _throughVersions[key] = _throughVersions.TryGetValue(key, out var existing)
                ? Math.Max(existing, version)
                : version;
        }
    }

    // The same rows as compaction's lag guard checks
    internal string[] ProgressionRowsToCheck() => AsyncShardProgressRows.For(_events, _tenantId);

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var schema = _events.DatabaseSchemaName;

        var sql = new StringBuilder();
        var parameters = new List<(object Value, NpgsqlDbType Type)>();

        void tenant(string alias)
        {
            if (!conjoined) return;
            sql.Append($" and {alias}.tenant_id = ?");
            parameters.Add((_tenantId, NpgsqlDbType.Varchar));
        }

        // The events that go cold, with the batch joined as b
        void coldEvents(string alias)
        {
            sql.Append($"{alias}.stream_id = b.id and {alias}.version <= b.through");
            tenant(alias);
        }

        const string allowed = "(select shards from lagging) is null";

        sql.Append("with batch as (select id, through from unnest(?, ?) as b(id, through)), ");
        parameters.Add((_throughVersions.Keys.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Varchar));
        parameters.Add((_throughVersions.Values.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Bigint));

        sql.Append($"target as (select max(e.seq_id) as seq from {schema}.mt_events e join batch b on ");
        coldEvents("e");
        sql.Append("), ");

        sql.Append("lagging as (select string_agg(n.name || ' (at ' || coalesce(p.last_seq_id, 0) || ')', ', ') as shards from unnest(?) as n(name) ");
        parameters.Add((ProgressionRowsToCheck(), NpgsqlDbType.Array | NpgsqlDbType.Varchar));
        sql.Append($"left join {_events.ProgressionTable.QualifiedName} p on p.name = n.name where coalesce(p.last_seq_id, 0) < (select seq from target)), ");

        // A cold store refuses tag types at startup, so there are no tag rows to delete today. The deletes are
        // still built the way compaction and the tombstone build them (#6049), so a tagged event can never be
        // left with tag rows its foreign key refuses to outlive.
        if (EventTagRowDeletes.TablesFor(_events).Count > 0)
        {
            EventTagRowDeletes.AppendCtes(_events,
                text => sql.Append(text),
                column =>
                {
                    sql.Append($"{allowed} and {column} in (select e.seq_id from {schema}.mt_events e join batch b on ");
                    coldEvents("e");
                    sql.Append(')');
                },
                () =>
                {
                    sql.Append('?');
                    parameters.Add((_tenantId, NpgsqlDbType.Varchar));
                });
            sql.Append(", ");
        }

        sql.Append($"cold_events as (delete from {schema}.mt_events e using batch b where {allowed} and ");
        coldEvents("e");
        sql.Append("), ");

        sql.Append($"marked as (update {schema}.{StreamsTable.TableName} s set {StreamsTable.ColdVersionColumn} = greatest(coalesce(s.{StreamsTable.ColdVersionColumn}, 0), b.through) from batch b where {allowed} and s.id = b.id");
        tenant("s");
        sql.Append(" and s.version >= b.through and s.is_archived = false returning s.id) ");

        sql.Append("select (select shards from lagging), ");
        sql.Append("(select array_agg(b.id || ' through ' || b.through order by b.id) from batch b where not exists (select 1 from marked m where m.id = b.id))");

        var npgsqlParameters = builder.AppendWithParameters(sql.ToString());
        for (var i = 0; i < npgsqlParameters.Length; i++)
        {
            npgsqlParameters[i].Value = parameters[i].Value;
            npgsqlParameters[i].NpgsqlDbType = parameters[i].Type;
        }
    }

    public Type DocumentType => typeof(IEvent);

    private string describeBatch()
    {
        if (_throughVersions.Count != 1) return $"{_throughVersions.Count} streams cold";
        var (key, version) = _throughVersions.First();
        return $"stream '{key}' cold through version {version}";
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        await reader.ReadAsync(token).ConfigureAwait(false);

        if (!await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            var shards = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            exceptions.Add(new InvalidOperationException(
                $"Cannot mark {describeBatch()} for tenant '{_tenantId}' yet: these async shards have not processed the events: {shards}. Nothing was deleted; try again once they caught up."));
            return;
        }

        if (!await reader.IsDBNullAsync(1, token).ConfigureAwait(false))
        {
            var missing = await reader.GetFieldValueAsync<string[]>(1, token).ConfigureAwait(false);
            exceptions.Add(new InvalidOperationException(
                $"Cannot mark {describeBatch()} for tenant '{_tenantId}': these streams do not exist for the tenant, are archived, or have not reached that version: {string.Join(", ", missing)}."));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}

/// <summary>
/// The last event operation of a unit of work that observed cold versions: fails it when the cold version
/// of an appended stream is no longer what the session saw. It runs behind the appends, whose
/// <c>mt_streams</c> updates hold the row locks, so a concurrent <c>MarkStreamCold</c> is either seen here or
/// waits for this transaction.
/// </summary>
internal class AssertColdVersionsOperation: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly string _tenantId;
    private readonly (string StreamKey, long ColdVersion)[] _expected;

    public AssertColdVersionsOperation(EventGraph events, string tenantId, (string StreamKey, long ColdVersion)[] expected)
    {
        _events = events;
        _tenantId = tenantId;
        _expected = expected;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append($"select s.id, coalesce(s.{StreamsTable.ColdVersionColumn}, 0) from unnest(");
        builder.AppendParameter(_expected.Select(x => x.StreamKey).ToArray());

        builder.Append(", ");
        builder.AppendParameter(_expected.Select(x => x.ColdVersion).ToArray());
        builder.Append($") as e(id, cold_version) join {_events.DatabaseSchemaName}.{StreamsTable.TableName} s on s.id = e.id");

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and s.tenant_id = ");
            builder.AppendParameter(_tenantId);
        }

        builder.Append($" where coalesce(s.{StreamsTable.ColdVersionColumn}, 0) <> e.cold_version");
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        var changed = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var id = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var coldVersion = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
            changed.Add($"'{id}' (now {(coldVersion > 0 ? $"cold through version {coldVersion}" : "without a cold part")})");
        }

        if (changed.Count > 0)
        {
            exceptions.Add(new ColdStreamChangedException(_tenantId, changed));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
