using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Progress;

internal class UpdateProjectionProgress: IStorageOperation, AssertsOnCallback, NoDataReturnedCall
{
    private readonly EventGraph _events;

    public UpdateProjectionProgress(EventGraph events, EventRange range)
    {
        Range = range;
        _events = events;
    }

    public EventRange Range { get; }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters =
            builder.AppendWithParameters(
                $"update {_events.ProgressionTable} set last_seq_id = ? where name = ? and last_seq_id = ?");

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
        if (reader.RecordsAffected ==
            0) // There's some weird quirks of the combined statements where this could be erroneously 2
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
