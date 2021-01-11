using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events.Daemon.Progress
{
    public class UpdateProjectionProgress: IStorageOperation
    {
        private readonly EventGraph _events;

        public UpdateProjectionProgress(EventGraph events)
        {
            _events = events;
        }

        public string ProjectionOrShardName { get; set; }
        public long StartingSequence { get; set; }
        public long UpdatedSequence { get; set; }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters =
                builder.AppendWithParameters($"update {_events.ProgressionTable} set last_seq_id = ? where name = ? and last_seq_id = ?");

            parameters[0].Value = UpdatedSequence;
            parameters[0].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[1].Value = ProjectionOrShardName;
            parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
            parameters[2].Value = StartingSequence;
            parameters[2].NpgsqlDbType = NpgsqlDbType.Bigint;
        }

        public Type DocumentType => typeof(ProjectionProgress);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            if (reader.RecordsAffected != 1)
            {
                throw new ProgressionProgressOutOfOrderException(ProjectionOrShardName);
            }
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            if (reader.RecordsAffected != 1)
            {
                throw new ProgressionProgressOutOfOrderException(ProjectionOrShardName);
            }

            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Events;
        }
    }
}
