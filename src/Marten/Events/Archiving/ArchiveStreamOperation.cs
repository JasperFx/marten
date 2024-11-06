using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Archiving;

internal class ArchiveStreamOperation: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly object _streamId;

    public ArchiveStreamOperation(EventGraph events, object streamId)
    {
        _events = events;
        _streamId = streamId;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            var parameters = builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{ArchiveStreamFunction.Name}(?, ?)");
            parameters[0].Value = _streamId;
            parameters[0].DbType = _events.StreamIdDbType;
            parameters[1].Value = session.TenantId;
        }
        else
        {
            var parameter =
                builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{ArchiveStreamFunction.Name}(?)")[0];
            parameter.Value = _streamId;

            parameter.DbType = _events.StreamIdDbType;
        }


    }


    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
