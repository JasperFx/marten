using System;
using System.Collections.Generic;
using System.Text;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Core.Exceptions;
using Marten.Events.Dcb;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Archiving;

/// <summary>
///     Calls <c>mt_archive_stream</c>, or with <c>archived: false</c> its reverse <c>mt_unarchive_stream</c>
///     (marten#5476). Both are queued on the session and run on <c>SaveChangesAsync</c>.
/// </summary>
/// <remarks>
///     With a DCB tag type registered, a second statement moves the <c>mt_dcb_tag_version</c> rows of the
///     stream's tags (#6078).
/// </remarks>
internal class ArchiveStreamOperation: IStorageOperation, IExceptionTransform
{
    private readonly EventGraph _events;
    private readonly object _streamId;
    private readonly bool _archived;

    public ArchiveStreamOperation(EventGraph events, object streamId): this(events, streamId, true)
    {
    }

    public ArchiveStreamOperation(EventGraph events, object streamId, bool archived)
    {
        _events = events;
        _streamId = streamId;
        _archived = archived;
    }

    private string functionName => _archived ? ArchiveStreamFunction.Name : ArchiveStreamFunction.UnArchiveName;

    public bool TryTransform(Exception original, out Exception transformed)
    {
        var pg = original as PostgresException ?? original.InnerException as PostgresException;
        if (!_archived && pg is { SqlState: ArchiveStreamFunction.LiveStreamCollisionSqlState })
        {
            transformed = new InvalidStreamOperationException(
                $"Cannot un-archive stream '{_streamId}' because a live stream with the same identity was started after it was archived. Archive or delete the live stream first.");
            return true;
        }

        transformed = original;
        return false;
    }

    public string? TenantId { get; set; }

    /// <summary>
    ///     With a cold event store, archiving a stream with a cold part is refused: a Live fetch reads the stream's
    ///     unarchived events only, so it would fold the cold part and nothing else.
    /// </summary>
    private bool refusesColdStreams => _archived && _events.ColdEventStore != null;

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var tenantId = TenantId ?? session.TenantId;

        if (refusesColdStreams)
        {
            writeArchiveUnlessCold(builder, tenantId);
        }
        else if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var parameters = builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{functionName}(?, ?)");
            parameters[0].Value = _streamId;
            parameters[0].NpgsqlDbType = _events.StreamIdDbType;
            parameters[1].Value = tenantId;
            parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
        }
        else
        {
            var parameter =
                builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{functionName}(?)")[0];
            parameter.Value = _streamId;

            parameter.NpgsqlDbType = _events.StreamIdDbType;
        }

        // #6078: after the flip, as a statement of its own. See StreamDcbTagKeys.AppendCreateOrMove.
        if (_events.TagTypes.Count > 0)
        {
            builder.StartNewCommand();
            appendDcbVersionMove(builder, tenantId);
        }
    }

    /// <summary>
    ///     #6078: archiving and un-archiving change which events a tag read returns, because a tag read leaves
    ///     archived events out. A DCB boundary only compares the <c>mt_dcb_tag_version</c> rows it captured, so
    ///     unless they move, a boundary that read the stream's events before an archive, or read without them
    ///     before an un-archive, saves straight through it. Every tag on the stream's events moves, and a tag
    ///     with no row gets one. This matches Polecat (polecat#841).
    /// </summary>
    private void appendDcbVersionMove(ICommandBuilder builder, string tenantId)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var sql = new StringBuilder();
        var parameters = new List<bool>(); // true = stream identity, false = tenant id

        StreamDcbTagKeys.AppendCreateOrMove(sql, parameters, _events, conjoined);

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
    ///     <code>
    ///     with cold as (select s.cold_version from mt_streams s where s.id = :id [and s.tenant_id = :tenant] for update),
    ///          archived as (select mt_archive_stream(:id[, :tenant]) where not exists (select 1 from cold where cold_version is not null))
    ///     select (select cold_version from cold), (select count(*) from archived)
    ///     </code>
    ///     The row lock makes the check exact against a concurrent <c>MarkStreamCold</c>, which updates the same row.
    /// </summary>
    private void writeArchiveUnlessCold(ICommandBuilder builder, string tenantId)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var schema = _events.DatabaseSchemaName;

        var sql = new StringBuilder();
        sql.Append($"with cold as (select s.{Schema.StreamsTable.ColdVersionColumn} as cold_version from {schema}.{Schema.StreamsTable.TableName} s where s.id = ?");
        if (conjoined) sql.Append(" and s.tenant_id = ?");
        sql.Append($" for update), archived as (select {schema}.{functionName}(?{(conjoined ? ", ?" : "")}) where not exists (select 1 from cold where cold_version is not null)) ");
        sql.Append("select (select cold_version from cold), (select count(*) from archived)");

        var parameters = builder.AppendWithParameters(sql.ToString());
        var index = 0;
        for (var i = 0; i < 2; i++)
        {
            parameters[index].Value = _streamId;
            parameters[index++].NpgsqlDbType = _events.StreamIdDbType;
            if (!conjoined) continue;

            parameters[index].Value = tenantId;
            parameters[index++].NpgsqlDbType = NpgsqlDbType.Varchar;
        }
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!refusesColdStreams) return;

        if (await reader.ReadAsync(token).ConfigureAwait(false) &&
            !await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            var coldVersion = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            exceptions.Add(new InvalidStreamOperationException(
                $"Stream '{_streamId}' cannot be archived: its events through version {coldVersion} are in cold storage, and a fetch of an archived stream would fold the cold part alone. Nothing was archived."));
        }
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
