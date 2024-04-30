# Open Telemetry and Metrics <Badge type="tip" text="7.10" />

Marten has built in support for emitting [Open Telemetry](https://opentelemetry.io/) spans and events, as well as for exporting metrics
using [System.Diagnostics.Metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation). 
This information should be usable with any monitoring system (Honeycomb, DataDog, Prometheus, SigNoz, Project Aspire, etc.)
that supports Open Telemetry. 

## Exporting Marten Data

**Heads up**, .NET processes will not actually emit any metrics or Open Telemetry data
they are collecting unless there are both configured exporters and you have explicitly
configured your application to emit this information. And note that you have to explicitly
export both metrics and [Open Telemetry](https://opentelemetry.io/) activity tracing independently. That all being
said, here's a sample of configuring the exporting -- this case just exporting information to
a Project Aspire dashboard in the end:

<!-- snippet: sample_enabling_open_telemetry_exporting_from_Marten -->
<a id='snippet-sample_enabling_open_telemetry_exporting_from_marten'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/AspireHeadlessTripService/Program.cs#L21-L40' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_open_telemetry_exporting_from_marten' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note, you'll need a reference to the `OpenTelemetry.Extensions.Hosting` Nuget for that
extension method. 

## Connection Events

::: warning
This connection tracking is bypassed by some uncommonly used operations, but will apply to basically every common
operation done with `IQuerySession` or `IDocumentSession`.
:::

It's often important just to track how many connections your application is using, and how long connections are being used.
To that end, you can opt into emitting Open Telemetry spans named *marten.connection* with this option:

<!-- snippet: sample_enabling_normal_level_of_connection_tracking -->
<a id='snippet-sample_enabling_normal_level_of_connection_tracking'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Track Marten connection usage
    opts.OpenTelemetry.TrackConnections = TrackLevel.Normal;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/OtelSamples.cs#L11-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_normal_level_of_connection_tracking' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This option will also tag events on the activity for any exceptions that happened from executing database commands. At a 
minimum, this option might help you spot performance issues due to chattiness between your application servers
and the database.

There is also a verbose mode that will also tag Open Telemetry activity events for all the Marten operations
(storing documents, appending events, etc.) performed by an `IDocumentSession.SaveChangesAsync()` call immediately
after successfully commiting a database transaction. This mode is probably most appropriate for troubleshooting
or performance testing where the extra information being emitted might help you spot database usage issues. That
option is shown below:

<!-- snippet: sample_enabling_verbose_level_of_connection_tracking -->
<a id='snippet-sample_enabling_verbose_level_of_connection_tracking'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Track Marten connection usage *and* all the "write" operations
    // that Marten does with that connection
    opts.OpenTelemetry.TrackConnections = TrackLevel.Verbose;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/OtelSamples.cs#L26-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_verbose_level_of_connection_tracking' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Event Store Metrics

You can opt into exporting a metrics counter for the events appended to Marten's event store functionality
with this option:

<!-- snippet: sample_track_event_counters -->
<a id='snippet-sample_track_event_counters'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Track the number of events being appended to the system
    opts.OpenTelemetry.TrackEventCounters();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/OtelSamples.cs#L42-L52' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_track_event_counters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This is adding a [Counter](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) metric named `marten.event.append`.
This metric has tags for:

* `event_type` - the Marten name for the type of the event within its configuration
* `tenant.id` - the tenant id for which the event was captured. If you are not using multi-tenancy, this value will be "*DEFAULT*" and can be just ignored

## Async Daemon Metrics and Spans

See [Open Telemetry and Metrics within the Async Daemon documentation](/events/projections/async-daemon.html#open-telemetry-and-metrics).



