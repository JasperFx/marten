using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Testing;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class HighWaterAgentTests: DaemonContext
{
    public HighWaterAgentTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task detect_when_running_after_events_are_posted()
    {
        NumberOfStreams = 10;

        Logger.LogDebug($"The expected high water mark at the end is " + NumberOfEvents);

        await PublishSingleThreaded();

        using var agent = await StartDaemon();

        await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

        agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

        await agent.StopAllAsync();
    }

    [Fact]
    public async Task detect_correctly_after_restarting_with_previous_state()
    {
        NumberOfStreams = 10;

        await PublishSingleThreaded();

        using var agent = await StartDaemon();

        await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

        agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

        await agent.StopAllAsync();

        using var agent2 = (ProjectionDaemon)await theStore.BuildProjectionDaemonAsync();
        await agent2.StartHighWaterDetectionAsync();
        await agent2.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

    }

    [Fact]
    public async Task detect_when_running_while_events_are_being_posted()
    {
        NumberOfStreams = 10;

        Logger.LogDebug($"The expected high water mark at the end is " + NumberOfEvents);



        using var agent = await StartDaemon();

        await PublishSingleThreaded();

        await agent.Tracker.WaitForShardState(new ShardState(ShardState.HighWaterMark, NumberOfEvents),
            30.Seconds());

        agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

        await agent.StopAllAsync();
    }



    [Fact]
    public async Task will_not_go_in_loop_when_sequence_is_advanced_but_gaps_from_high_water_to_end()
    {
        NumberOfStreams = 10;
        await PublishSingleThreaded();
        theStore.Options.Projections.StaleSequenceThreshold = 1.Seconds();

        using var agent = await StartDaemon();

        await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 2.Minutes());
        await agent.StopAllAsync();

        using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"SELECT setval('daemon.mt_events_sequence', {NumberOfEvents + 5});").ExecuteNonQueryAsync();
            await conn.CloseAsync();
        }

        using var agent2 = await StartDaemon();

        await agent2.Tracker.WaitForHighWaterMark(NumberOfEvents + 5);
    }

    private async Task deleteEvents(params long[] ids)
    {
        await using var conn = theStore.CreateConnection();
        await conn.OpenAsync();

        await conn
            .CreateCommand($"delete from {theStore.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(:ids)")
            .With("ids", ids, NpgsqlDbType.Bigint | NpgsqlDbType.Array)
            .ExecuteNonQueryAsync();
    }
}
