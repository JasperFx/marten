#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Dcb;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// The hard delete behind <c>IEventStoreOperations.TombstoneStream</c> (marten#5673, jasperfx#1014):
/// removes a stream, every event in it, every DCB tag row on those events, and the
/// <c>mt_dcb_tag_version</c> row of every tag value nothing else carries.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT named after the contract member. In Marten "tombstone" already means the
/// sequence-gap bookkeeping stream (<see cref="Tombstone"/>, <see cref="EstablishTombstoneStream"/>),
/// which has nothing to do with deleting a stream.
/// </para>
/// <para>
/// The delete is one statement, a chain of data-modifying CTEs, rather than one command per table. Npgsql
/// will not take <c>;</c>-separated statements in one positional-parameter command. Every CTE runs against
/// the same snapshot, so the tag delete still sees the events it is keyed by, and the foreign keys (tag
/// rows to events, events to the stream) are checked at the end of the statement, when all of them are
/// gone. Natural key rows cascade from <c>mt_streams</c>.
/// </para>
/// <para>
/// #6035: with a DCB tag type registered, a second command runs first and moves the stream's
/// <c>mt_dcb_tag_version</c> rows, and the delete then erases the rows no other stream carries. It has to
/// be a command of its own; see <see cref="appendDcbVersionMove"/>.
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
        => ConfigureCommand(builder, TenantId ?? session.TenantId);

    /// <summary>
    /// The same commands without a session, for <c>IDocumentCleaner.DeleteSingleEventStreamAsync</c> (#6078).
    /// </summary>
    internal void ConfigureCommand(ICommandBuilder builder, string tenantId)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
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

        // #6035: a statement of its own, ahead of the delete. See appendDcbVersionMove.
        if (_events.TagTypes.Count > 0)
        {
            appendDcbVersionMove(sql, parameters, conjoined);
            bindParameters(builder.AppendWithParameters(sql.ToString()), parameters, tenantId);
            builder.StartNewCommand();

            sql.Clear();
            parameters.Clear();
        }

        sql.Append("with ");

        // Tag rows first in intent: they reference the events. Built by the same code as compaction's (#6049).
        // The subquery reads the same snapshot as the events delete below, so it still finds the stream's
        // sequence numbers.
        if (EventTagRowDeletes.TablesFor(_events).Count > 0)
        {
            EventTagRowDeletes.AppendCtes(_events, text => sql.Append(text),
                seqId =>
                {
                    sql.Append($"{seqId} in (select seq_id from {schema}.mt_events where ");
                    streamPredicate("stream_id");
                    sql.Append(')');
                },
                () =>
                {
                    sql.Append('?');
                    parameters.Add(false);
                });
            sql.Append(", ");
        }

        sql.Append($"doomed_events as (delete from {schema}.mt_events where ");
        streamPredicate("stream_id");
        sql.Append(')');

        appendDcbVersionErasure(sql, parameters, conjoined);

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

        bindParameters(builder.AppendWithParameters(sql.ToString()), parameters, tenantId);
    }

    private void bindParameters(NpgsqlParameter[] npgsqlParameters, List<bool> parameters, string tenantId)
    {
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
    /// #6035: move the version of every <c>mt_dcb_tag_version</c> row the stream's events reach, to a new
    /// value from the sequence. A boundary that read those events captured that version, so without this
    /// it saved straight through the tombstone as if nothing it read had changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A statement of its own, ahead of the delete, and that is what makes the delete safe.</b> PostgreSQL
    /// gives each statement one snapshot. Once this holds the rows, whoever had one has committed and their
    /// events can be seen by the NEXT statement, which is where the delete asks whether another stream still
    /// carries the tag. In one statement that question would be answered from a snapshot taken before the
    /// wait, and a concurrent append that committed meanwhile would be missed.
    /// </para>
    /// <para>
    /// It never creates a row. A tag with no row stays without one, because the tombstone is erasing the
    /// value. A boundary that read events under such a tag captured 0, and its save counts those events
    /// again instead (<see cref="Marten.Events.Dcb.DcbBoundaryRead"/>).
    /// </para>
    /// <para>
    /// The stream's own row is locked first. An append to this stream holds it (its events' foreign key)
    /// before it takes any version row, so taking the two in the same order keeps them from deadlocking.
    /// The version rows are locked in ordinal order, the order appends lock them in.
    /// </para>
    /// </remarks>
    private void appendDcbVersionMove(StringBuilder sql, List<bool> parameters, bool conjoined)
    {
        var schema = _events.DatabaseSchemaName;
        var table = $"{schema}.{DcbTagVersionTable.TableName}";

        sql.Append($"with stream_row as (select 1 from {schema}.{StreamsTable.TableName} where id = ?");
        parameters.Add(true);
        if (conjoined)
        {
            sql.Append(" and tenant_id = ?");
            parameters.Add(false);
        }

        sql.Append(" for update), stream_tags as (");
        var parts = StreamDcbTagKeys.PartCount(_events);
        for (var i = 0; i < parts; i++)
        {
            if (i > 0) sql.Append(" union all ");
            sql.Append("select k, v from (");
            StreamDcbTagKeys.Append(sql, parameters, _events, conjoined, i);
            sql.Append($") k{i}");
        }

        sql.Append($"), locked as (select r.tag_table, r.tag_value, r.tenant_id from {table} r where exists (select 1 from stream_row)");
        appendVersionRowTenant(sql, parameters, conjoined);
        sql.Append(" and (r.tag_table, r.tag_value) in (select k, v from stream_tags)");
        sql.Append(" order by r.tag_table collate \"C\", r.tag_value collate \"C\", r.tenant_id collate \"C\" for update) ");
        sql.Append($"update {table} r set version = {DcbTagVersionTable.NextVersionSql(_events)} from locked l ");
        sql.Append("where r.tag_table = l.tag_table and r.tag_value = l.tag_value and r.tenant_id = l.tenant_id");
    }

    /// <summary>
    /// #6087: which tenant's <c>mt_dcb_tag_version</c> rows are the stream's. Under conjoined tenancy, the stream's
    /// tenant. Otherwise the database holds one stream per id whatever tenant wrote it, and the rows carry the
    /// tenant of the session that appended (the real tenant id in a database-per-tenant store, not
    /// <c>*DEFAULT*</c>), so the rows are the stream's by tag alone. Matching on the tenant there missed them
    /// whenever the delete ran without the right tenant, which <c>DeleteSingleEventStreamAsync(id)</c> always does.
    /// </summary>
    private static void appendVersionRowTenant(StringBuilder sql, List<bool> parameters, bool conjoined)
    {
        if (!conjoined) return;

        sql.Append(" and r.tenant_id = ?");
        parameters.Add(false);
    }

    /// <summary>
    /// #6035: delete the <c>mt_dcb_tag_version</c> row of every tag on the stream that no other stream in the
    /// tenant (or, not conjoined, in the database) carries, so the value doesn't outlive the events it came from. A row another stream carries is
    /// shared by value and stays, already moved by <see cref="appendDcbVersionMove"/>. An archived stream
    /// counts as carrying its tags, because its events are still stored.
    /// </summary>
    /// <remarks>
    /// A CTE of the delete, so it reads the stream's events and tag rows from the same snapshot that deletes
    /// them. That snapshot is taken after the move took the rows. A save waiting for one of them hasn't
    /// committed; if this deletes the row it then creates it again with a new value, or, if it captured the
    /// old one, loses.
    /// </remarks>
    private void appendDcbVersionErasure(StringBuilder sql, List<bool> parameters, bool conjoined)
    {
        if (_events.TagTypes.Count == 0) return;

        var parts = StreamDcbTagKeys.PartCount(_events);
        for (var i = 0; i < parts; i++)
        {
            sql.Append($", dcb_erase{i} as (delete from {_events.DatabaseSchemaName}.{DcbTagVersionTable.TableName} r using (");
            StreamDcbTagKeys.Append(sql, parameters, _events, conjoined, i);
            sql.Append(") s where r.tag_table = s.k and r.tag_value = s.v");
            appendVersionRowTenant(sql, parameters, conjoined);
            sql.Append(" and not exists (");
            appendCarriedElsewhere(sql, parameters, conjoined, i);
            sql.Append("))");
        }
    }

    // Another stream in the tenant with an event carrying the tag, of the same event type for a
    // per-event-type key (s.type is null on the tag type's own key).
    private void appendCarriedElsewhere(StringBuilder sql, List<bool> parameters, bool conjoined, int tagTable)
    {
        var schema = _events.DatabaseSchemaName;

        if (_events.DcbStorageMode == DcbStorageMode.HStore)
        {
            sql.Append($"select 1 from {schema}.mt_events o where o.tags @> hstore(s.suffix, s.v)");
        }
        else
        {
            var suffix = _events.TagTypes[tagTable].TableSuffix;
            sql.Append($"select 1 from {schema}.mt_event_tag_{suffix} c join {schema}.mt_events o on o.seq_id = c.seq_id");
            if (conjoined) sql.Append(" and o.tenant_id = c.tenant_id");
            sql.Append(" where c.value = s.raw");
        }

        sql.Append(" and (s.type is null or o.type = s.type) and o.stream_id <> ?");
        parameters.Add(true);
        if (conjoined)
        {
            sql.Append(" and o.tenant_id = ?");
            parameters.Add(false);
        }
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
