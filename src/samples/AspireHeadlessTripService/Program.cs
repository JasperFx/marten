using DaemonTests;
using DaemonTests.TestingSupport;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

var builder = Host.CreateApplicationBuilder();

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var endpointUri = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
Console.WriteLine("OLTP endpoint: " + endpointUri);

builder.Services.AddOpenTelemetry().UseOtlpExporter();

// The following lines enable the Prometheus exporter (requires the OpenTelemetry.Exporter.Prometheus.AspNetCore package)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Marten");
    })
    // BUG: Part of the workaround for https://github.com/open-telemetry/opentelemetry-dotnet-contrib/issues/1617
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Marten");
    });

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
