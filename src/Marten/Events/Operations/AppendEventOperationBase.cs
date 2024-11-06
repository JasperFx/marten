using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

// Leave public for codegen!
public abstract class AppendEventOperationBase: IStorageOperation, NoDataReturnedCall
{
    public AppendEventOperationBase(StreamAction stream, IEvent e)
    {
        Stream = stream;
        Event = e;

        if (e.Version == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(e), "Version cannot be 0");
        }
    }

    public StreamAction Stream { get; }
    public IEvent Event { get; }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        ConfigureCommandSpecific((IPostgresqlCommandBuilder)builder, (IMartenSession)session);
    }

    public abstract void ConfigureCommandSpecific(IPostgresqlCommandBuilder builder, IMartenSession session);

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }

    public override string ToString()
    {
        return $"Insert Event to Stream {Stream.Key ?? Stream.Id.ToString()}, Version {Event.Version}";
    }
}

