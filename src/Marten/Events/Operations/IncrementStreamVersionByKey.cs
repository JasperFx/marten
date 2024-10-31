using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

internal class IncrementStreamVersionByKey: IStorageOperation
{
    private readonly EventGraph _events;

    public IncrementStreamVersionByKey(EventGraph events, StreamAction stream)
    {
        _events = events;
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("update ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_events set version = version + ");
        builder.Append(Stream.Events.Count.ToString());
        builder.Append(" where id = ");
        builder.AppendParameter(Stream.Key);
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {

    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}