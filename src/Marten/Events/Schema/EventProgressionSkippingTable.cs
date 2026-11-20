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

    /// <summary>
    ///     #6007: the <c>tenant_id</c> of a skip of the store-global high water mark, which covers every tenant. A
    ///     skip of one tenant's own mark under per-tenant partitioned events carries that tenant's id. The empty
    ///     string rather than null because the column is part of the primary key, as for
    ///     <see cref="ProjectionFloorTable.GlobalTenant" />.
    /// </summary>
    public const string GlobalTenant = "";

    public const string PrimaryKeyConstraint = "pkey_mt_high_water_skips_ending_sequence_tenant_id";

    public EventProgressionSkippingTable(EventGraph eventGraph) : base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        AddColumn<long>("ending_sequence").AsPrimaryKey();

        // #6007: per-tenant partitioned events give every tenant its own sequence, so two tenants can skip
        // ranges with the same ending sequence
        AddColumn("tenant_id", "varchar").DefaultValueByString(GlobalTenant).NotNull().AsPrimaryKey();
        AddColumn<long>("starting_sequence").NotNull();
        AddColumn("timestamp", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");

        // #6007: why the range was skipped (HighWaterSkipReason), the sequence numbers inside it that had no
        // committed event when it was skipped (inclusive bounds as pairs: first1, last1, first2, last2, ...), and
        // when the daemon stopped checking those numbers for events that commit late
        AddColumn("reason", "varchar").AllowNulls();
        AddColumn("gaps", "bigint[]").AllowNulls();
        AddColumn("closed_at", "timestamp with time zone").AllowNulls();
    }
}

/// <summary>
///     #6007: one event that committed inside a sequence range the high water mark had already skipped, so async
///     projections and subscriptions that were past it never saw it.
/// </summary>
internal class LateEventsTable: Table
{
    public const string Name = "mt_high_water_late_events";

    public LateEventsTable(EventGraph eventGraph): base(new PostgresqlObjectName(eventGraph.DatabaseSchemaName, Name))
    {
        AddColumn<long>("seq_id").AsPrimaryKey();
        AddColumn("tenant_id", "varchar").NotNull().AsPrimaryKey();
        AddColumn("stream_id", "varchar").NotNull();
        AddColumn("event_type", "varchar").NotNull();
        AddColumn<long>("skip_starting_sequence").NotNull();
        AddColumn<long>("skip_ending_sequence").NotNull();
        AddColumn("skip_reason", "varchar").AllowNulls();
        AddColumn("detected_at", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");

        // The async single-stream projections that have taken the event in, by rebuilding its stream, or that do not
        // handle it
        AddColumn("repaired_projections", "varchar[]").AllowNulls();
    }
}

/// <summary>
///     #6007: why the high water mark skipped a sequence range, as recorded in <c>mt_high_water_skips.reason</c>.
/// </summary>
internal enum HighWaterSkipReason
{
    /// <summary>No transaction that could fill the range was alive. An event committing inside it is a detector bug.</summary>
    Proven,

    /// <summary>A transaction that could fill the range looked alive, and <c>SkipStaleGapsDespiteLiveTransactionsAfter</c> expired.</summary>
    LiveTransaction,

    /// <summary>Skipped on the stale threshold alone, without transaction evidence (<c>UseTransactionEvidenceForGapSkipping = false</c>).</summary>
    WallClock,

    /// <summary>An operator advanced the mark to the committed ceiling.</summary>
    Manual
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

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("");
        builder.AppendParameter(_limit);
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
