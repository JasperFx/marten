using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class HotCold_leadership_election : DaemonContext
    {
        public HotCold_leadership_election(ITestOutputHelper output) : base(output)
        {

        }


        [Fact]
        public async Task detect_high_water_mark_when_running_after_events_are_posted()
        {
            NumberOfStreams = 10;

            Logger.LogDebug($"The expected high water mark at the end is " + NumberOfEvents);

            await PublishSingleThreaded();

            using var agent = await StartDaemonInHotColdMode();

            await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            agent.IsRunning.ShouldBeTrue();

            await agent.StopAll();
        }

        [Fact]
        public async Task end_to_end_with_events_already_published()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x =>
            {
                x.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
                x.Logger(new TestOutputMartenLogger(_output));
            }, true);

            var agent = await StartDaemonInHotColdMode();
            var waiter = agent.Tracker.WaitForShardState("Trip:All", NumberOfEvents, 15.Seconds());


            await PublishSingleThreaded();

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }

        [Fact]
        public async Task end_to_end_with_events_already_published_several_daemons()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x =>
            {
                x.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
            }, true);

            var agent = await StartDaemonInHotColdMode();
            var daemon2 = await StartAdditionalDaemonInHotColdMode();

            var waiter = agent.Tracker.WaitForShardState("Trip:All", NumberOfEvents, 30.Seconds());


            await PublishSingleThreaded();

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }

        private async Task assertIsRunning(ProjectionDaemon daemon, TimeSpan timeout)
        {
            if (daemon.IsRunning) return;

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (stopwatch.Elapsed < timeout && !daemon.IsRunning)
            {
                await Task.Delay(200.Milliseconds());
            }

            stopwatch.Stop();
            daemon.IsRunning.ShouldBeTrue();
        }

        private async Task<ProjectionDaemon> findRunningDaemon(params ProjectionDaemon[] daemons)
        {
            var timeout = 5.Seconds();

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (stopwatch.Elapsed < timeout)
            {
                var daemon = daemons.SingleOrDefault(x => x.IsRunning);
                if (daemon != null) return daemon;
                await Task.Delay(200.Milliseconds());
            }

            stopwatch.Stop();
            throw new Exception("Could not determine that a Daemon was started in the time allotted");
        }

        [Fact]
        public async Task one_additional_node_will_take_over_leadership_mechanics()
        {
            var daemon1 = await StartDaemonInHotColdMode();

            await assertIsRunning(daemon1, 1.Seconds());

            var daemon2 = await StartDaemonInHotColdMode();
            await Task.Delay(500.Milliseconds());

            daemon1.IsRunning.ShouldBeTrue();
            daemon2.IsRunning.ShouldBeFalse();

            await daemon1.StopAll();

            daemon1.IsRunning.ShouldBeFalse();

            await assertIsRunning(daemon2, 3.Seconds());
        }

        [Fact]
        public async Task spin_up_several_daemons_and_fail_over()
        {
            var daemon1 = await StartDaemonInHotColdMode();

            await assertIsRunning(daemon1, 1.Seconds());

            var others = new ProjectionDaemon[4];

            others[0] = await StartDaemonInHotColdMode();
            others[1] = await StartDaemonInHotColdMode();
            others[2] = await StartDaemonInHotColdMode();
            others[3] = await StartDaemonInHotColdMode();

            await daemon1.StopAll();

            var active = await findRunningDaemon(others);

            foreach (var other in others)
            {
                if (other.Equals(active)) continue;

                other.IsRunning.ShouldBeFalse();
            }


        }
    }
}
