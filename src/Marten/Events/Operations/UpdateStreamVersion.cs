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
using Weasel.Core.Operations;
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

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        ConfigureCommandSpecific((IPostgresqlCommandBuilder)builder, (IMartenSession)session);
    }

    public abstract void ConfigureCommandSpecific(IPostgresqlCommandBuilder builder, IMartenSession session);

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (reader.RecordsAffected != 0)
        {
            return;
        }

        var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Key ?? (object)Stream.Id, Stream.AggregateType,
            Stream.ExpectedVersionOnServer.Value, -1);
        exceptions.Add(ex);
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        Postprocess(reader, exceptions);

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
