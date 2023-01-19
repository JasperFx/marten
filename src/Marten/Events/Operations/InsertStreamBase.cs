using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

// Leave public for codegen!
public abstract class InsertStreamBase: IStorageOperation, IExceptionTransform
{
    public InsertStreamBase(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
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
        return $"InsertStream: {Stream.Key ?? Stream.Id.ToString()}";
    }

    private static bool matches(Exception e)
    {
        return e.Message.Contains("23505: duplicate key value violates unique constraint") &&
               e.Message.Contains("streams");
    }

    public bool TryTransform(Exception original, out Exception transformed)
    {
        if (original is MartenCommandException mce)
        {
            if (mce.InnerException != null &&
                matches(mce.InnerException))
            {
                transformed =
                    new ExistingStreamIdCollisionException((object)Stream.Key ?? Stream.Id, Stream.AggregateType);
                return true;
            }
        }

        if (matches(original))
        {
            transformed =
                new ExistingStreamIdCollisionException((object)Stream.Key ?? Stream.Id, Stream.AggregateType);
            return true;
        }

        transformed = original;
        return false;
    }
}
