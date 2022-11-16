using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
#nullable enable

namespace Marten.Events.Archiving
{
    internal class ArchiveStreamOperation : IStorageOperation
    {
        private readonly EventGraph _events;
        private readonly object _streamId;

        public ArchiveStreamOperation(EventGraph events, object streamId, string? tenantId)
        {
            _events = events;
            _streamId = streamId;
            TenantId = tenantId;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameter = builder.AppendWithParameters($"select {_events.DatabaseSchemaName}.{ArchiveStreamFunction.Name}(?)")[0];
            parameter.Value = _streamId;

            parameter.NpgsqlDbType = _events.StreamIdDbType;
        }


        public Type DocumentType => typeof(IEvent);
        public string? TenantId { get; }

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
