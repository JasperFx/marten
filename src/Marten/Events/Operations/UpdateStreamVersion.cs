using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

// Leave public for codegen!
public abstract class UpdateStreamVersion: IStorageOperation
{
    public UpdateStreamVersion(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public Type DocumentType => typeof(IEvent);
    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (reader.RecordsAffected != 0)
        {
            return Task.CompletedTask;
        }

        var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Key ?? (object)Stream.Id, Stream.AggregateType,
            Stream.ExpectedVersionOnServer.Value, -1);
        exceptions.Add(ex);

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
