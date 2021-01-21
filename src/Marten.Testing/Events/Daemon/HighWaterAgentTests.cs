using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Daemon
{
    public class HighWaterAgentTests: DaemonContext
    {
        private readonly ITestOutputHelper _output;

        public HighWaterAgentTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task detect_when_running_after_events_are_posted()
        {
            NumberOfStreams = 10;

            _output.WriteLine($"The expected high water mark at the end is " + NumberOfEvents);

            await PublishSingleThreaded();

            var logger = new TestLogger<IProjection>(_output);
            using var agent = new NodeAgent(theStore, logger);

            agent.Start();

            var statistics = await theStore.Events.FetchStatistics();

            await agent.Tracker.WaitForShardState(new ShardState(ShardState.HighWaterMark, statistics.EventCount),
                10.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            await agent.StopAll();
        }

        [Fact]
        public async Task detect_when_running_while_events_are_being_posted()
        {
            NumberOfStreams = 100;

            _output.WriteLine($"The expected high water mark at the end is " + NumberOfEvents);



            var logger = new TestLogger<IProjection>(_output);
            using var agent = new NodeAgent(theStore, logger);

            agent.Start();

            await PublishSingleThreaded();

            await agent.Tracker.WaitForShardState(new ShardState(ShardState.HighWaterMark, NumberOfEvents),
                30.Seconds());

            agent.Tracker.HighWaterMark.ShouldBe(NumberOfEvents);

            await agent.StopAll();
        }
    }
}
