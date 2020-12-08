using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events
{
    internal class TombstoneTable: Table
    {
        public TombstoneTable(EventGraph events) : base(new DbObjectName(events.DatabaseSchemaName, "mt_event_tombstones"))
        {
            AddPrimaryKey(new TableColumn("sequence", "bigint", "NOT NULL"));
            AddColumn("timestamp", "timestamptz", "default (now()) NOT NULL");
        }
    }

    internal class InsertTombstone: IStorageOperation
    {
        private readonly long _sequence;
        private readonly EventGraph _events;

        public InsertTombstone(long sequence, EventGraph events)
        {
            _sequence = sequence;
            _events = events;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters($"insert into {_events.DatabaseSchemaName}.mt_event_tombstones (sequence) values (?)");
            parameters[0].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[1].Value = _sequence;
        }

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
    }
}
