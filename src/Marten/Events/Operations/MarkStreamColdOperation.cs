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
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Behind <c>IEventStoreOperations.MarkStreamCold</c>: deletes the stream's events through
/// <c>throughVersion</c> from <c>mt_events</c> (and their DCB tag rows), and records the boundary in
/// <c>mt_streams.cold_version</c>.
/// </summary>
/// <remarks>
/// <para>
/// One statement of data-modifying CTEs, for the reasons on <see cref="DeleteStreamOperation" />. The boundary
/// only ever moves up. The statement always returns one row, so the unit of work fails when the stream does
/// not exist, has not reached <c>throughVersion</c>, or when an async shard has not processed the events yet.
/// </para>
/// <para>
/// The lag guard compares every registered async shard's <c>mt_event_progression</c> row with the highest
/// <c>seq_id</c> about to be deleted, and deletes nothing when one is behind or has no row. Row names are
/// composed exactly as the daemon composes them: per tenant (<c>{shard}:{tenant}</c>) when the store
/// distributes agents per tenant, which is <c>UseTenantPartitionedEvents</c>, and store-global otherwise, in
/// which case <c>seq_id</c> is store-global too. A shard that is merely between batches has not committed
/// progress past those events, so it is counted as behind -- the guard never lets a delete overtake a shard.
/// </para>
/// </remarks>
internal class MarkStreamColdOperation: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly object _streamId;
    private readonly long _throughVersion;
    private readonly string _tenantId;

    public MarkStreamColdOperation(EventGraph events, object streamId, long throughVersion, string tenantId)
    {
        if (throughVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(throughVersion), "A stream's cold part has at least version 1.");
        }

        _events = events;
        _streamId = streamId;
        _throughVersion = throughVersion;
        _tenantId = tenantId;
    }

    internal string[] ProgressionRowsToCheck()
    {
        var perTenant = _events.UseTenantPartitionedEvents;
        return _events.Options.Projections.AllShards()
            .Select(x => perTenant
                ? ShardName.Compose(x.Name.Name, x.Name.ShardKey, _tenantId, x.Name.Version).Identity
                : x.Name.Identity)
            .Distinct()
            .ToArray();
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var schema = _events.DatabaseSchemaName;

        var sql = new StringBuilder();
        var parameters = new List<(object Value, NpgsqlDbType Type)>();

        void streamPredicate(string column)
        {
            sql.Append(column).Append(" = ?");
            parameters.Add((_streamId, _events.StreamIdDbType));
            if (conjoined)
            {
                sql.Append(" and tenant_id = ?");
                parameters.Add((_tenantId, NpgsqlDbType.Varchar));
            }
        }

        void coldEventsPredicate()
        {
            streamPredicate("stream_id");
            sql.Append(" and version <= ?");
            parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        }

        const string allowed = "(select shards from lagging) is null";

        sql.Append($"with target as (select max(seq_id) as seq from {schema}.mt_events where ");
        coldEventsPredicate();
        sql.Append("), ");

        sql.Append("lagging as (select string_agg(n.name || ' (at ' || coalesce(p.last_seq_id, 0) || ')', ', ') as shards from unnest(?) as n(name) ");
        parameters.Add((ProgressionRowsToCheck(), NpgsqlDbType.Array | NpgsqlDbType.Varchar));
        sql.Append($"left join {_events.ProgressionTable.QualifiedName} p on p.name = n.name where coalesce(p.last_seq_id, 0) < (select seq from target)), ");

        var tagTables = _events.DcbStorageMode == DcbStorageMode.HStore
            ? (IReadOnlyList<JasperFx.Events.Tags.ITagTypeRegistration>)Array.Empty<JasperFx.Events.Tags.ITagTypeRegistration>()
            : _events.TagTypes;

        for (var i = 0; i < tagTables.Count; i++)
        {
            sql.Append($"tag{i} as (delete from {schema}.mt_event_tag_{tagTables[i].TableSuffix} where {allowed} and ");
            if (conjoined)
            {
                sql.Append("tenant_id = ? and ");
                parameters.Add((_tenantId, NpgsqlDbType.Varchar));
            }

            sql.Append($"seq_id in (select seq_id from {schema}.mt_events where ");
            coldEventsPredicate();
            sql.Append(")), ");
        }

        sql.Append($"cold_events as (delete from {schema}.mt_events where {allowed} and ");
        coldEventsPredicate();
        sql.Append("), ");

        sql.Append($"marked as (update {schema}.{StreamsTable.TableName} set {StreamsTable.ColdVersionColumn} = greatest(coalesce({StreamsTable.ColdVersionColumn}, 0), ?) where {allowed} and ");
        parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        streamPredicate("id");
        sql.Append(" and version >= ?");
        parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        sql.Append(" returning version) ");

        sql.Append("select (select shards from lagging), exists (select 1 from marked)");

        var npgsqlParameters = builder.AppendWithParameters(sql.ToString());
        for (var i = 0; i < npgsqlParameters.Length; i++)
        {
            npgsqlParameters[i].Value = parameters[i].Value;
            npgsqlParameters[i].NpgsqlDbType = parameters[i].Type;
        }
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        await reader.ReadAsync(token).ConfigureAwait(false);

        if (!await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            var shards = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            exceptions.Add(new InvalidOperationException(
                $"Cannot mark stream '{_streamId}' cold through version {_throughVersion} for tenant '{_tenantId}' yet: these async shards have not processed its events: {shards}. Nothing was deleted; try again once they caught up."));
            return;
        }

        if (!await reader.GetFieldValueAsync<bool>(1, token).ConfigureAwait(false))
        {
            exceptions.Add(new InvalidOperationException(
                $"Cannot mark stream '{_streamId}' cold through version {_throughVersion}: the stream does not exist for tenant '{_tenantId}', or has not reached that version."));
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
    private readonly (object StreamId, long ColdVersion)[] _expected;

    public AssertColdVersionsOperation(EventGraph events, string tenantId, (object StreamId, long ColdVersion)[] expected)
    {
        _events = events;
        _tenantId = tenantId;
        _expected = expected;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append($"select s.id, coalesce(s.{StreamsTable.ColdVersionColumn}, 0) from unnest(");
        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            builder.AppendParameter(_expected.Select(x => (Guid)x.StreamId).ToArray());
        }
        else
        {
            builder.AppendParameter(_expected.Select(x => (string)x.StreamId).ToArray());
        }

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
            var id = await reader.GetFieldValueAsync<object>(0, token).ConfigureAwait(false);
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
