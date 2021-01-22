using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Daemon
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

            using var agent = await StartNodeAgent();

            await agent.Tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            await agent.StopAll();
        }

        [Fact]
        public async Task detect_when_running_while_events_are_being_posted()
        {
            NumberOfStreams = 10;

            Logger.LogDebug($"The expected high water mark at the end is " + NumberOfEvents);



            using var agent = await StartNodeAgent();

            await PublishSingleThreaded();

            await agent.Tracker.WaitForShardState(new ShardState(ShardState.HighWaterMark, NumberOfEvents),
                30.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            await agent.StopAll();
        }
    }
}
