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

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var tenantId = TenantId ?? session.TenantId;

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
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
