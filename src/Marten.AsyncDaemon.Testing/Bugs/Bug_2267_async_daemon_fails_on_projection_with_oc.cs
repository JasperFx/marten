using System;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class Bug_2267_async_daemon_fails_on_projection_with_oc: DaemonContext
    {
        public Bug_2267_async_daemon_fails_on_projection_with_oc(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task test_async_daemon_on_projection_with_optimistic_concurrency()
        {
            StoreOptions(opts =>
            {
                opts.Projections.AsyncMode = DaemonMode.Solo;
                opts.Projections.Add<TripProjection>(ProjectionLifecycle.Async);
                opts.Schema.For<Trip>().UseOptimisticConcurrency(true);
            });


            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(Guid.NewGuid(), new TripStarted { Day = 1 });
                await session.SaveChangesAsync();
            }

            using var daemon = await StartDaemon();
            await daemon.Tracker.WaitForShardState("Trip:All", 1, 3.Seconds());
        }
    }

    public class TripProjection: SingleStreamAggregation<Trip>
    {
        public Trip Create(TripStarted started)
            => new Trip { StartedOn = started.Day, Active = true };
    }
}
