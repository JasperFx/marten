using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #4749 — after a failed append (duplicate key from concurrent identical PUTs under AppendMode.Quick)
/// the global mt_events_sequence advances past the last committed event, leaving a permanent gap
/// (sequence = N+1 while max committed seq_id = N). QueryForNonStaleData then computes a high-water
/// ceiling of N+1 that the async projection can never reach, so every call waits the full timeout.
/// This pins (1) that the wait surfaces a clean TimeoutException, and (2) the opt-in overload that
/// returns the latest available data instead of throwing.
/// </summary>
public class Bug_4749_query_for_non_stale_data_sequence_gap: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public Bug_4749_query_for_non_stale_data_sequence_gap(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));
    }

    private async Task SeedCatchUpAndCreateSequenceGap()
    {
        for (var i = 0; i < 4; i++)
        {
            theSession.Events.StartStream(new AEvent(), new BEvent(), new CEvent());
            theSession.Events.StartStream(new DEvent(), new AEvent());
        }
        await theSession.SaveChangesAsync();

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(10.Seconds());
        }

        // Simulate the #4749 gap: a failed append advanced the sequence without committing an event,
        // so mt_events_sequence is now ahead of the highest committed seq_id. The async projection has
        // already caught up to the real high-water, but FetchHighestEventSequenceNumber reads the
        // (now unreachable) sequence value.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var schema = theStore.Options.Events.DatabaseSchemaName;
        await conn.CreateCommand($"select setval('{schema}.mt_events_sequence', (select last_value from {schema}.mt_events_sequence) + 5)")
            .ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task default_overload_throws_a_clean_timeout_exception()
    {
        await SeedCatchUpAndCreateSequenceGap();

        var ex = await Record.ExceptionAsync(() =>
            theSession.QueryForNonStaleData<SimpleAggregate>(2.Seconds()).ToListAsync());

        _output.WriteLine(ex?.GetType().FullName ?? "<no exception>");
        ex.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task return_stale_data_overload_returns_latest_available_instead_of_throwing()
    {
        await SeedCatchUpAndCreateSequenceGap();

        // The projection caught up to the real high-water before the gap, so the materialized data is
        // present — the only problem is the unreachable sequence ceiling. With ReturnStaleData the query
        // must return that data rather than throw.
        var items = await theSession
            .QueryForNonStaleData<SimpleAggregate>(2.Seconds(), NonStaleDataTimeoutMode.ReturnStaleData)
            .ToListAsync();

        items.Count.ShouldBe(8);
    }
}
