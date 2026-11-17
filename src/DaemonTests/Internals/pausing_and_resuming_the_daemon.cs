using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

public class pausing_and_resuming_the_daemon: HostedStoreContext
{
    [Fact]
    public async Task stop_and_resume_from_the_host_extensions()
    {
        var host = await StartHostAsync(
            opts =>
            {
                opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);

                // weasel#869: Weasel cannot read back the indexes of a partitioned table in a schema whose name
                // contains "using" ("pausing_and_resuming_the_daemon" does). Archived-stream partitioning is on
                // by default since 10.0, so keep mt_events unpartitioned here until that is fixed.
                opts.Events.UseArchivedStreamPartitioning = false;
            },
            DaemonMode.Solo);

        await host.PauseAllDaemonsAsync();

        await host.ResumeAllDaemonsAsync();

        await using var session = host.DocumentStore().LightweightSession();
        var id = session.Events.StartStream<TestingSupport.TripProjection>(new TripStarted()).Id;

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await host.WaitForNonStaleProjectionDataAsync(15.Seconds());

        var trip = await session.LoadAsync<Trip>(id, TestContext.Current.CancellationToken);
        trip.ShouldNotBeNull();
    }
}
