using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Daemon.Progress
{
    internal class InsertProjectionProgress: IStorageOperation
    {
        private readonly EventGraph _events;
        private readonly EventRange _progress;

        public InsertProjectionProgress(EventGraph events, EventRange progress)
        {
            _events = events;
            _progress = progress;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters =
                builder.AppendWithParameters($"insert into {_events.ProgressionTable} (name, last_seq_id) values (?, ?)");

            parameters[0].Value = _progress.ShardName.Identity;
            parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar;
            parameters[1].Value = _progress.SequenceCeiling;
            parameters[1].NpgsqlDbType = NpgsqlDbType.Bigint;
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
