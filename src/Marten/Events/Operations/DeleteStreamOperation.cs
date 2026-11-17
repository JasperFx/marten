#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// The hard delete behind <c>IEventStoreOperations.TombstoneStream</c> (marten#5673, jasperfx#1014):
/// removes a stream, every event in it and every DCB tag row on those events.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT named after the contract member. In Marten "tombstone" already means the
/// sequence-gap bookkeeping stream (<see cref="Tombstone"/>, <see cref="EstablishTombstoneStream"/>),
/// which has nothing to do with deleting a stream.
/// </para>
/// <para>
/// One statement, a chain of data-modifying CTEs, rather than one command per table. Npgsql will not
/// take <c>;</c>-separated statements in one positional-parameter command, and starting extra batch
/// commands from inside an operation breaks <c>OperationPage</c>'s one-command-per-operation
/// alignment. Every CTE runs against the same snapshot, so the tag delete still sees the events it is
/// keyed by, and the foreign keys (tag rows to events, events to the stream) are checked at the end
/// of the statement, when all of them are gone. Natural key rows cascade from <c>mt_streams</c>.
/// </para>
/// <para>
/// Deleting through the parent tables reaches the archived partition under
/// <c>UseArchivedStreamPartitioning</c>, and the tenant predicate prunes to the tenant's partition
/// under <c>UseTenantPartitionedEvents</c>. The event sequence is never touched, so deleted sequence
/// numbers are never handed out again.
/// </para>
/// </remarks>
internal class DeleteStreamOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly object _streamId;

    public DeleteStreamOperation(EventGraph events, object streamId, string? tenantId)
    {
        _events = events;
        _streamId = streamId;
        TenantId = tenantId;
    }

    public string? TenantId { get; }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var tenantId = TenantId ?? session.TenantId;
        var schema = _events.DatabaseSchemaName;

        // Each placeholder is either the stream identity or the tenant id; record which, in order.
        var sql = new StringBuilder();
        var parameters = new List<bool>(); // true = stream identity, false = tenant id

        void streamPredicate(string column)
        {
            sql.Append(column).Append(" = ?");
            parameters.Add(true);
            if (conjoined)
            {
                sql.Append(" and tenant_id = ?");
                parameters.Add(false);
            }
        }

        sql.Append("with ");

        var tagTables = _events.DcbStorageMode == DcbStorageMode.HStore
            ? (IReadOnlyList<JasperFx.Events.Tags.ITagTypeRegistration>)Array.Empty<JasperFx.Events.Tags.ITagTypeRegistration>()
            : _events.TagTypes;

        // Tag rows first in intent: they reference the events. The subquery reads the same snapshot
        // as the events delete below, so it still finds the stream's sequence numbers.
        for (var i = 0; i < tagTables.Count; i++)
        {
            sql.Append($"tag{i} as (delete from {schema}.mt_event_tag_{tagTables[i].TableSuffix} where ");
            if (conjoined)
            {
                sql.Append("tenant_id = ? and ");
                parameters.Add(false);
            }

            sql.Append($"seq_id in (select seq_id from {schema}.mt_events where ");
            streamPredicate("stream_id");
            sql.Append(") returning value, seq_id), ");
        }

        sql.Append($"doomed_events as (delete from {schema}.mt_events where ");
        streamPredicate("stream_id");
        sql.Append(_events.DcbStorageMode == DcbStorageMode.HStore ? " returning seq_id, type, tags)" : " returning seq_id, type)");

        appendDcbVersionBump(sql, parameters, tagTables);

        if (_events.EnableStrictStreamIdentityEnforcement)
        {
            // Without this the identity stays claimed and starting a new stream with it is refused as
            // a collision, which would make the delete a soft one.
            sql.Append($", doomed_identity as (delete from {schema}.{StreamIdentityEnforcementTable.TableName} where ");
            streamPredicate("id");
            sql.Append(')');
        }

        sql.Append($" delete from {schema}.{StreamsTable.TableName} where ");
        streamPredicate("id");

        var npgsqlParameters = builder.AppendWithParameters(sql.ToString());
        for (var i = 0; i < npgsqlParameters.Length; i++)
        {
            if (parameters[i])
            {
                npgsqlParameters[i].Value = _streamId;
                npgsqlParameters[i].NpgsqlDbType = _events.StreamIdDbType;
            }
            else
            {
                npgsqlParameters[i].Value = tenantId;
                npgsqlParameters[i].NpgsqlDbType = NpgsqlDbType.Varchar;
            }
        }
    }

    /// <summary>
    /// #6035: move the DCB version of every tag value the deleted events carried, the same version an append
    /// of those events moved. A boundary that read this stream's tagged events captured that version, so
    /// without the bump it saved straight through the tombstone as if nothing it read had changed. The row is
    /// created if it is somehow missing, as an append would, rather than leaving a captured 0 matching.
    /// </summary>
    /// <remarks>
    /// The values come back from the deletes themselves: each tag table's <c>value</c> cast to text, which
    /// is <see cref="JasperFx.Events.Tags.TagValueStringifier"/>'s form for the supported tag types (text,
    /// uuid, integer, bigint, smallint), or the hstore pairs, which are written in that form. Rows are taken
    /// in ordinal order, the order appends lock them in.
    /// </remarks>
    private void appendDcbVersionBump(StringBuilder sql, List<bool> parameters,
        IReadOnlyList<JasperFx.Events.Tags.ITagTypeRegistration> tagTables)
    {
        if (_events.TagTypes.Count == 0) return;

        var schema = _events.DatabaseSchemaName;
        var recordEventTypes = _events.DcbConflictScope != DcbConflictScope.TagValue;

        var sources = new List<string>();
        if (_events.DcbStorageMode == DcbStorageMode.HStore)
        {
            sources.Add("select t.key as k, t.value as v from doomed_events d cross join lateral each(d.tags) t");
            if (recordEventTypes)
            {
                sources.Add(
                    "select t.key || ':' || d.type, t.value from doomed_events d cross join lateral each(d.tags) t");
            }
        }
        else
        {
            for (var i = 0; i < tagTables.Count; i++)
            {
                var suffix = tagTables[i].TableSuffix;
                sources.Add($"select '{suffix}' as k, t.value::text as v from tag{i} t");
                if (recordEventTypes)
                {
                    sources.Add(
                        $"select '{suffix}:' || d.type, t.value::text from tag{i} t join doomed_events d on d.seq_id = t.seq_id");
                }
            }
        }

        if (sources.Count == 0) return;

        sql.Append($", dcb_bump as (insert into {schema}.mt_dcb_tag_version (tag_table, tag_value, tenant_id, version) ");
        sql.Append("select k, v, ?, 1 from (select distinct k, v from (");
        parameters.Add(false);
        sql.Append(string.Join(" union all ", sources));
        sql.Append(") tagged) bumps order by k collate \"C\", v collate \"C\" ");
        sql.Append("on conflict (tag_table, tag_value, tenant_id) do update set version = mt_dcb_tag_version.version + 1)");
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
