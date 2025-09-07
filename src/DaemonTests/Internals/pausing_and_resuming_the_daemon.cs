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

public class pausing_and_resuming_the_daemon
{
    [Fact]
    public async Task stop_and_resume_from_the_host_extensions()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "coordinator";

                    opts.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async);
                }).AddAsyncDaemon(DaemonMode.Solo);
            }).StartAsync();

        await host.PauseAllDaemonsAsync();

        await host.ResumeAllDaemonsAsync();

        await using var session = host.DocumentStore().LightweightSession();
        var id = session.Events.StartStream<TestingSupport.TripProjection>(new TripStarted()).Id;

        await session.SaveChangesAsync();

        await host.WaitForNonStaleProjectionDataAsync(15.Seconds());

        var trip = await session.LoadAsync<Trip>(id);
        trip.ShouldNotBeNull();
    }
}
