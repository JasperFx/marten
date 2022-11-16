using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
using NpgsqlTypes;
using Weasel.Core;

#nullable enable

namespace Marten.Events.Operations
{
    internal class EventProgressWrite: IStorageOperation
    {
        private readonly string _key;
        private readonly long _number;
        private readonly DbObjectName _sproc;

        public EventProgressWrite(EventGraph events, string key, long number, string? tenantId)
        {
            _sproc = new DbObjectName(events.DatabaseSchemaName, "mt_mark_event_progression");
            _key = key;
            _number = number;
            TenantId = tenantId;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var nameArg = builder.AddParameter(_key, NpgsqlDbType.Varchar);
            var numberArg = builder.AddParameter(_number, NpgsqlDbType.Bigint);
            builder.Append($"select {_sproc}(:{nameArg.ParameterName}, :{numberArg.ParameterName})");
        }

        public Type DocumentType => typeof(IEvent);
        public string? TenantId { get; }

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
            return OperationRole.Other;
        }
    }
}
