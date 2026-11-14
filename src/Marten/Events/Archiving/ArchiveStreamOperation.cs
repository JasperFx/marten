using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Core.Exceptions;
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
        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var parameters = builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{functionName}(?, ?)");
            parameters[0].Value = _streamId;
            parameters[0].NpgsqlDbType = _events.StreamIdDbType;
            parameters[1].Value = TenantId ?? session.TenantId;
            parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
        }
        else
        {
            var parameter =
                builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{functionName}(?)")[0];
            parameter.Value = _streamId;

            parameter.NpgsqlDbType = _events.StreamIdDbType;
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
