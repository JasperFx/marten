using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Daemon.Progress
{
    internal class UpdateProjectionProgress: IStorageOperation
    {
        public EventRange Range { get; }
        private readonly EventGraph _events;

        public UpdateProjectionProgress(EventGraph events, EventRange range)
        {
            Range = range;
            _events = events;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters =
                builder.AppendWithParameters($"update {_events.ProgressionTable} set last_seq_id = ? where name = ? and last_seq_id = ?");

            parameters[0].Value = Range.SequenceCeiling;
            parameters[0].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[1].Value = Range.ShardName.Identity;
            parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
            parameters[2].Value = Range.SequenceFloor;
            parameters[2].NpgsqlDbType = NpgsqlDbType.Bigint;
        }

        public Type DocumentType => typeof(IEvent);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            if (reader.RecordsAffected != 1)
            {
                throw new ProgressionProgressOutOfOrderException(Range.ShardName);
            }
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            if (reader.RecordsAffected != 1)
            {
                throw new ProgressionProgressOutOfOrderException(Range.ShardName);
            }

            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Events;
        }
    }
}
