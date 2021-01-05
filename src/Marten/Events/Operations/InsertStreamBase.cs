using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Util;

namespace Marten.Events.Operations
{
    public abstract class InsertStreamBase : IStorageOperation, IExceptionTransform
    {
        public InsertStreamBase(StreamAction stream)
        {
            Stream = stream;
        }

        public StreamAction Stream { get; }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public Type DocumentType => typeof(StreamAction);
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

        public bool TryTransform(Exception original, out Exception transformed)
        {
            if (original is MartenCommandException mce)
            {
                if (mce.InnerException != null && mce.InnerException.Message.Contains("23505: duplicate key value violates unique constraint") && mce.InnerException.Message.Contains("streams"))
                {
                    transformed = new ExistingStreamIdCollisionException((object) Stream.Key ?? Stream.Id, Stream.AggregateType);
                    return true;
                }
            }

            transformed = original;
            return false;
        }
    }


}
