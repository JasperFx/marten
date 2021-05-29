using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Archiving
{
    internal class ArchiveStreamOperation : IStorageOperation
    {
        private readonly EventGraph _events;
        private readonly object _streamId;

        public ArchiveStreamOperation(EventGraph events, object streamId)
        {
            _events = events;
            _streamId = streamId;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameter = builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{ArchiveStreamFunction.Name}(?)")[0];
            parameter.Value = _streamId;

            parameter.NpgsqlDbType = _events.StreamIdDbType;
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
}
