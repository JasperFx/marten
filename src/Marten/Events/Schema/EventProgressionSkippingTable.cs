using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventProgressionSkippingTable: Table
{
    public const string Name = "mt_high_water_skips";

    public EventProgressionSkippingTable(EventGraph eventGraph) : base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        AddColumn<long>("ending_sequence").AsPrimaryKey();
        AddColumn<long>("starting_sequence").NotNull();
        AddColumn("timestamp", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");
    }
}

public record HighWaterDetectionSkip(long Ending, long Starting, DateTimeOffset Timestamp);

internal class EventProgressionSkipsHandler : ISingleQueryHandler<IReadOnlyList<HighWaterDetectionSkip>>
{
    private readonly EventGraph _graph;
    private readonly int _limit;

    public EventProgressionSkipsHandler(EventGraph graph, int limit)
    {
        _graph = graph;
        _limit = limit;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("");
        builder.AppendParameter(_limit);
    }

    public IReadOnlyList<HighWaterDetectionSkip> Handle(DbDataReader reader, IMartenSession session)
    {
        throw new NotSupportedException();
    }

    public NpgsqlCommand BuildCommand()
    {
        return new NpgsqlCommand(
                $"select ending_sequence, starting_sequence, timestamp from {_graph.DatabaseSchemaName}.{EventProgressionSkippingTable.Name} order by ending_sequence desc limit :limit")
            .With("limit", _limit);
    }

    public async Task<IReadOnlyList<HighWaterDetectionSkip>> HandleAsync(DbDataReader reader, CancellationToken token)
    {
        var list = new List<HighWaterDetectionSkip>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var ending = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            var starting = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);
            var timestamp = await reader.GetFieldValueAsync<DateTimeOffset>(2, token).ConfigureAwait(false);

            list.Add(new HighWaterDetectionSkip(ending, starting, timestamp));
        }

        return list;
    }
}
