using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
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
    }
}
