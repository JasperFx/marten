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

#region sample_enabling_open_telemetry_exporting_from_Marten

// This is passed in by Project Aspire. The exporter usage is a little
// different for other tools like Prometheus or SigNoz
var endpointUri = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
Console.WriteLine("OLTP endpoint: " + endpointUri);

builder.Services.AddOpenTelemetry().UseOtlpExporter();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("Marten");
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Marten");
    });

#endregion

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
