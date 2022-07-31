using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Testing;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
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

            await agent.StopAll();
        }

        [Fact]
        public async Task detect_correctly_after_restarting_with_previous_state()
        {
            NumberOfStreams = 10;

            await PublishSingleThreaded();

            using var agent = await StartDaemon();

            await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            await agent.StopAll();

            using var agent2 = new ProjectionDaemon(theStore, new NulloLogger());
            await agent2.StartDaemon();
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

            await agent.StopAll();
        }

        [Fact]
        public async Task ensures_all_gaps_are_delayed()
        {
            NumberOfStreams = 10;
            await PublishSingleThreaded();
            theStore.Options.Projections.StaleSequenceThreshold = 10.Seconds();
            await deleteEvents(NumberOfEvents-50, NumberOfEvents - 100);
            var start = Stopwatch.StartNew();
            using var agent = await StartDaemon();

            await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 2.Minutes());

            start.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(20));
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
}
