using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Util;

namespace Marten.Events.Operations
{
    public abstract class UpdateStreamVersion : IStorageOperation
    {
        public StreamAction Stream { get; }

        public UpdateStreamVersion(StreamAction stream)
        {
            Stream = stream;
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public Type DocumentType => typeof(StreamAction);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            if (reader.RecordsAffected != 0) return;

            var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Key ?? (object)Stream.Id, Stream.AggregateType, Stream.ExpectedVersionOnServer.Value, -1);
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
}
