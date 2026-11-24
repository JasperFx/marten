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

<!-- snippet: sample_enabling_open_telemetry_exporting_from_marten -->
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/samples/AspireHeadlessTripService/Program.cs#L22-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_enabling_open_telemetry_exporting_from_marten' title='Start of snippet'>anchor</a></sup>
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
after successfully committing a database transaction. This mode is probably most appropriate for troubleshooting
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

Marten exports a metrics counter for the events appended to Marten's event store functionality. This is on by default
since Marten 10; before that you opted in with `TrackEventCounters()`, which still works. The switch is:

<!-- snippet: sample_track_event_counters -->
<a id='snippet-sample_track_event_counters'></a>
```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Track the number of events being appended to the system.
    // On by default since Marten 10, set to false to opt out
    opts.OpenTelemetry.EnableEventCounters = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/OtelSamples.cs#L42-L53' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_track_event_counters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This is adding a [Counter](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) metric named `marten.event.append`.
This metric has tags for:

* `event.type` - the Marten name for the type of the event within its configuration
* `tenant.id` - the tenant id for which the event was captured. If you are not using multi-tenancy, this value will be "*DEFAULT*" and can be just ignored

## Turning the Default Metrics Off

The `marten.event.append` counter and the `marten.fetch_for_writing.events_replayed` histogram below are the two
metrics Marten 10 turned on by default. `opts.RestoreV9Defaults()` turns both off again, along with the other
[Marten 10 default changes](/migration-guide#restoring-v9-defaults). To turn them off without the rest, set
`opts.OpenTelemetry.EnableEventCounters` and `opts.OpenTelemetry.EnableFetchForWritingMetrics` to `false`.

## Custom Counters on Committed Changes

You can add counters of your own that are recorded after every successful `SaveChangesAsync()`. Each one is created
on the `Marten` meter, and your callback receives the counter and the `IChangeSet` that was just committed, with
its inserted, updated and deleted documents and its appended events:

<!-- snippet: sample_export_counter_on_change_sets -->
<a id='snippet-sample_export_counter_on_change_sets'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // A counter on Marten's "Marten" meter, recorded after every
    // successful SaveChangesAsync()
    opts.OpenTelemetry.ExportCounterOnChangeSets<long>(
        "billing.invoices.inserted",
        "invoices",
        (counter, commit) =>
        {
            var invoices = commit.Inserted.OfType<ConfigDocsInvoice>().Count();
            if (invoices > 0)
            {
                counter.Add(invoices);
            }
        });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/Examples/ConfigurationDocsSamples.cs#L328-L349' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_export_counter_on_change_sets' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The callback runs after the transaction has committed, so an exception thrown from it is logged rather than
reported to the caller of `SaveChangesAsync()`, and the other counters are still recorded. `marten.event.append` is
itself one of these counters.

## FetchForWriting Metrics <Badge type="tip" text="9.24" />

The cost of a command handler that uses [`FetchForWriting`](/scenarios/command_handler_workflow) is
mostly the cost of rebuilding the aggregate, and that in turn is however many events Marten had to read
back and fold. How many that is depends entirely on the aggregate's projection lifecycle, and it is not
something you can see from the outside. Marten measures it by default since 10.0. Before that you opted in with
`TrackFetchForWritingMetrics()`, which still works. The switch is:

```cs
using var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // How many events does each FetchForWriting() call actually replay?
    // On by default since Marten 10, set to false to opt out
    opts.OpenTelemetry.EnableFetchForWritingMetrics = true;
});
```

This adds a [Histogram](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation)
named `marten.fetch_for_writing.events_replayed`, recorded once per `FetchForWriting()` call, with tags for:

* `aggregate.type` - the .NET type name of the aggregate that was fetched
* `fetch.plan` - which plan served the call: `Live`, `Inline` or `Async`

Reading it is straightforward, because each plan has a characteristic shape:

| `fetch.plan` | What the histogram shows |
| --- | --- |
| `Live` | The full stream length, every single time — the aggregate is rebuilt from scratch on each fetch |
| `Async` | Only the events the async daemon has not projected yet, so this doubles as a per-fetch view of daemon lag |
| `Inline` | Always zero — the stored snapshot is current by construction |

The histogram's own count gives you fetch frequency for free, so a `sum by (aggregate.type)` tells you
which aggregates dominate your event replay, and the `Live` percentiles tell you what switching one of
them to `Inline` would actually save before you commit to that migration.

::: tip
With `EnableFetchForWritingMetrics = false` the histogram is never created, so turning it off leaves a null check
on the fetch path and nothing more. With it on and nothing listening to the meter, a fetch checks the histogram's
`Enabled` flag and records nothing. Both tag values are cached per aggregate type, so a recorded fetch
allocates nothing on the heap either.
:::

## Async Daemon Metrics and Spans

See [Open Telemetry and Metrics within the Async Daemon documentation](/events/projections/async-daemon.html#open-telemetry-and-metrics).

The daemon also has a counter named `marten.daemon.late_events`, with one count for each event that committed inside a
sequence range the high water mark had already skipped. It is tagged with:

* `event.type` - the event type's name
* `tenant.id` - the event's tenant id
* `skip.reason` - why the range was skipped: `Proven`, `LiveTransaction`, `WallClock` or `Manual`, or `Unknown` when no reason was recorded

A healthy store never records one, so the counter is only created when the first late event is found. It needs
`opts.Events.EnableAdvancedAsyncTracking`, which is on by default, and `RestoreV9Defaults()` does not turn it off. See
[Events that commit late](/events/projections/async-daemon#events-that-commit-late).
