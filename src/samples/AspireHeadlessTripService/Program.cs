using DaemonTests;
using DaemonTests.TestingSupport;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddMarten(opts =>
{
    opts.DatabaseSchemaName = "cli";

    opts.MultiTenantedWithSingleServer(
        ConnectionSource.ConnectionString,
        t => t.WithTenants("tenant1", "tenant2", "tenant3")
    );

    // Register all event store projections ahead of time
    opts.Projections
        .Add(new TripProjectionWithCustomName(), ProjectionLifecycle.Async);

    opts.Projections
        .Add(new DayProjection(), ProjectionLifecycle.Async);

    opts.Projections
        .Add(new DistanceProjection(), ProjectionLifecycle.Async);


}).AddAsyncDaemon(DaemonMode.Solo);

await builder.Build().RunAsync();
