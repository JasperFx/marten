# Async Projections Daemon

The *Async Daemon* is the nickname for Marten's built in asynchronous projection processing engine. The current async daemon from Marten V4 on requires no other infrastructure
besides Postgresql and Marten itself. The daemon itself runs inside an [IHostedService](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-5.0&tabs=visual-studio) implementation in your application. The **daemon is disabled by default**.

The *Async Daemon* will process events **in order** through all projections registered with an
asynchronous lifecycle.

First, some terminology:

* *Projection* -- a projected view defined by the `IProjection` interface and registered with Marten. See also [Projections](/events/projections/).
* *Projection Shard* -- a logical segment of events that are executed separately by the async daemon
* *High Water Mark* -- the furthest known event sequence that the daemon "knows" that all events with that sequence or lower can be safely processed in order by projections. The high water mark will frequently be a little behind the highest known event sequence number if outstanding gaps in the event sequence are detected.

There are only two basic things to configure the *Async Daemon*:

1. Register the projections that should run asynchronously
2. Set the `StoreOptions.AsyncMode` to either `Solo` or `HotCold` (more on what these options mean later in this page)

:::warning
The asynchronous daemon service registration is **opt in** starting with V5 and requires the chained call
to `AddAsyncDaemon()` shown below. This was done to alleviate user issues with Marten inside of Azure Functions
where the runtime was not compatible with the hosted service for the daemon.
:::

As an example, this configures the daemon to run in the current node with a single active projection:

<!-- snippet: sample_bootstrap_daemon_solo -->
<a id='snippet-sample_bootstrap_daemon_solo'></a>
```cs
var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
            {
                opts.Connection("some connection string");

                // Register any projections you need to run asynchronously
                opts.Projections.Add<TripProjectionWithCustomName>(ProjectionLifecycle.Async);
            })
            // Turn on the async daemon in "Solo" mode
            .AddAsyncDaemon(DaemonMode.Solo);
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L19-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrap_daemon_solo' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Likewise, we can configure the daemon to run in *HotCold* mode like this:

<!-- snippet: sample_bootstrap_daemon_hotcold -->
<a id='snippet-sample_bootstrap_daemon_hotcold'></a>
```cs
var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
            {
                opts.Connection("some connection string");

                // Register any projections you need to run asynchronously
                opts.Projections.Add<TripProjectionWithCustomName>(ProjectionLifecycle.Async);
            })
            // Turn on the async daemon in "HotCold" mode
            // with built in leader election
            .AddAsyncDaemon(DaemonMode.HotCold);
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L90-L108' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrap_daemon_hotcold' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Solo vs. HotCold

::: tip
Marten's leader election is done with Postgresql advisory locks, so there is no additional software infrastructure necessary other than
Postgresql and Marten itself.
:::

::: tip
The "HotCold" mode was substantially changed for Marten 7.0 and will potentially run projections across different nodes
:::

As of right now, the daemon can run as one of two modes:

1. *Solo* -- the daemon will be automatically started when the application is bootstrapped and all projections and projection shards will be started on that node. The assumption with Solo
   is that there is never more than one running system node for your application.
1. *HotCold* -- the daemon will use a built in [leader election](https://en.wikipedia.org/wiki/Leader_election) function individually for each
   projection on each tenant database and **ensure that each projection is running on exactly one running process**.

Regardless of how things are configured, the daemon is designed to detect when multiple running processes are updating the same projection shard and will shut down the process if concurrency issues persist.

If your Marten store is only using a single database, Marten will distribute projection by projection. If your store is using
[separate databases for multi-tenancy](/configuration/multitenancy), the async daemon will group all projections for a single
database on the same executing node as a purposeful strategy to reduce the total number of connections to the databases.

::: tip
The built in capability of Marten to distribute projections is somewhat limited, and it's still likely that all projections
will end up running on the first process to start up. If your system requires better load distribution for increased scalability,
contact [JasperFx Software](https://jasperfx.net) about their "Critter Stack Pro" product.
:::

## Daemon Logging

The daemon logs through the standard .Net `ILogger` interface service registered in your application's underlying DI container. In the case of the daemon having to skip
"poison pill" events, you can see a record of this in the `DeadLetterEvent` storage in your database (the `mt_doc_deadletterevent` table) along with the exception. Use this to fix underlying issues
and be able to replay events later after the fix.

## Error Handling

::: warning
The async daemon error handling was rewritten for Marten 7.0. The new model uses
[Polly](https://www.thepollyproject.org/) for typical transient errors like network hiccups or a database being too
busy. Marten does have some configuration to alternatively skip certain errors in normal background operation or while
doing rebuilds.
:::

**In all examples, `opts` is a `StoreOptions` object. Besides the basic [Polly error handling](/configuration/retries#resiliency-policies),
you have these three options to configure error handling within your system's usage of asynchronous projections:

<!-- snippet: sample_async_daemon_error_policies -->
<a id='snippet-sample_async_daemon_error_policies'></a>
```cs
using var host = await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddMarten(opts =>
            {
                // connection information...

                opts.Projections.Errors.SkipApplyErrors = true;
                opts.Projections.Errors.SkipSerializationErrors = true;
                opts.Projections.Errors.SkipUnknownEvents = true;

                opts.Projections.RebuildErrors.SkipApplyErrors = false;
                opts.Projections.RebuildErrors.SkipSerializationErrors = false;
                opts.Projections.RebuildErrors.SkipUnknownEvents = false;
            })
            .AddAsyncDaemon(DaemonMode.HotCold);
    }).StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/ErrorHandling.cs#L13-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_async_daemon_error_policies' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

| Option                    | Description                                                                                                                      | Continuous Default | Rebuild Default |
|---------------------------|----------------------------------------------------------------------------------------------------------------------------------|--------------------|-----------------|
| `SkipApplyErrors`         | Should errors that occur in projection code (i.e., not Marten or PostgreSQL related errors) be skipped during Daemon processing? | True               | False           |
| `SkipSerializationErrors` | Should errors from serialization or upcasters be ignored and that event skipped during processing?                               | True               | False           |
| `SkipUnknownEvents`       | Should unknown event types be skipped by the daemon?                                                                             | True               | False           |

In all cases, if a serialization, apply, or unknown error is encountered and Marten is not configured to skip that type of 
error, the individual projection will be paused. In the case of projection rebuilds, this will immediately stop the rebuild
operation. By default, all of these errors are skipped during continuous processing and enforced during rebuilds.

::: tip
Skipping unknown event types is important for "blue/green" deployment of system changes where a new application version
introduces an entirely new event type.
:::

## Poison Event Detection

See the section on error handling. Poison event detection is a little more automatically integrated into Marten 7.0.

## Accessing the Executing Async Daemon

New in Marten 7.0 is the ability to readily access the executing instance of the daemon for each database in your system.
You can use this approach to track progress or start or stop individual projections like so:

<!-- snippet: sample_using_projection_coordinator -->
<a id='snippet-sample_using_projection_coordinator'></a>
```cs
public static async Task accessing_the_daemon(IHost host)
{
    // This is a new service introduced by Marten 7.0 that
    // is automatically registered as a singleton in your
    // application by IServiceCollection.AddMarten()

    var coordinator = host.Services.GetRequiredService<IProjectionCoordinator>();

    // If targeting only a single database with Marten
    var daemon = coordinator.DaemonForMainDatabase();
    await daemon.StopAgentAsync("Trip:All");

    // If targeting multiple databases for multi-tenancy
    var daemon2 = await coordinator.DaemonForDatabase("tenant1");
    await daemon.StopAllAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/DaemonUsage.cs#L10-L29' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_projection_coordinator' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Testing Async Projections <Badge type="tip" text="7.0" />

::: tip
This method works by polling the progress tables in the database, so it's usable regardless of where or how you've started
up the async daemon in your code.
:::

Asynchronous projections can be a little rough to test because of the timing issues (is the daemon finished with my new events yet?).
To that end, Marten introduced an extension method called `IDocumentStore.WaitForNonStaleProjectionDataAsync()` to help your tests "wait" until any asynchronous
projections are caught up to the latest events posted at the time of the call.

You can see the usage below from one of the Marten tests where we use that method to just wait until the running projection
daemon has caught up:

<!-- snippet: sample_using_WaitForNonStaleProjectionDataAsync -->
<a id='snippet-sample_using_waitfornonstaleprojectiondataasync'></a>
```cs
[Fact]
public async Task run_simultaneously()
{
    StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

    NumberOfStreams = 10;

    var agent = await StartDaemon();

    // This method publishes a random number of events
    await PublishSingleThreaded();

    // Wait for all projections to reach the highest event sequence point
    // as of the time this method is called
    await theStore.WaitForNonStaleProjectionDataAsync(15.Seconds());

    await CheckExpectedResults();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/event_projections_end_to_end.cs#L43-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_waitfornonstaleprojectiondataasync' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The basic idea in your tests is to:

1. Start the async daemon running continuously
1. Set up your desired system state by appending events as the test input
1. Call the `WaitForNonStaleProjectionDataAsync()` method **before** checking the expected outcomes of the test

There is also another overload to wait for just one tenant database in the case of using a database per tenant. The default
overload **will wait for the daemon of all known databases to catch up to the latest sequence.**

### Accessing the daemon from IHost:

If you're integration testing with the `IHost` (e.g. using Alba) object, you can access the daemon and wait for non stale data like this:

<!-- snippet: sample_accessing_daemon_from_ihost -->
<a id='snippet-sample_accessing_daemon_from_ihost'></a>
```cs
[Fact]
public async Task run_simultaneously()
{
    var host = await StartDaemonInHotColdMode();

    StoreOptions(x => x.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

    NumberOfStreams = 10;

    var agent = await StartDaemon();

    // This method publishes a random number of events
    await PublishSingleThreaded();

    // Wait for all projections to reach the highest event sequence point
    // as of the time this method is called
    await host.WaitForNonStaleProjectionDataAsync(15.Seconds());

    await CheckExpectedResults();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/event_projections_end_to_end_ihost.cs#L22-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_accessing_daemon_from_ihost' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Diagnostics

The following code shows the diagnostics support for the async daemon as it is today:

<!-- snippet: sample_DaemonDiagnostics -->
<a id='snippet-sample_daemondiagnostics'></a>
```cs
public static async Task ShowDaemonDiagnostics(IDocumentStore store)
{
    // This will tell you the current progress of each known projection shard
    // according to the latest recorded mark in the database
    var allProgress = await store.Advanced.AllProjectionProgress();
    foreach (var state in allProgress)
    {
        Console.WriteLine($"{state.ShardName} is at {state.Sequence}");
    }

    // This will allow you to retrieve some basic statistics about the event store
    var stats = await store.Advanced.FetchEventStoreStatistics();
    Console.WriteLine($"The event store highest sequence is {stats.EventSequenceNumber}");

    // This will let you fetch the current shard state of a single projection shard,
    // but in this case we're looking for the daemon high water mark
    var daemonHighWaterMark = await store.Advanced.ProjectionProgressFor(new ShardName(ShardState.HighWaterMark));
    Console.WriteLine($"The daemon high water sequence mark is {daemonHighWaterMark}");
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L111-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_daemondiagnostics' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Command Line Support

If you're using [Marten's command line support](/configuration/cli), you have the new `projections` command to help
manage the daemon at development or even deployment time.

To just start up and run the async daemon for your application in a console window, use:

```bash
dotnet run -- projections
```

To interactively select which projections to run, use:

```bash
dotnet run -- projections -i
```

or

```bash
dotnet run -- projections --interactive
```

To list out all the known projection shards, use:

```bash
dotnet run -- projections --list
```

To run a single projection, use:

```bash
dotnet run -- projections --projection [shard name]
```

or

```bash
dotnet run -- projections -p [shard name]
```

To rebuild all the known projections with both asynchronous and inline lifecycles, use:

```bash
dotnet run -- projections --rebuild
```

To interactively select which projections to rebuild, use:

```bash
dotnet run -- projections -i --rebuild
```

To rebuild a single projection at a time, use:

```bash
dotnet run -- projections --rebuild -p [shard name]
```

If you are using multi-tenancy with multiple Marten databases, you can choose to rebuild the
projections for only one tenant database -- but note that this will rebuild the entire database
across all the tenants in that database -- by using the `--tenant` flag like so:

```bash
dotnet run -- projections --rebuild --tenant tenant1
```

## Using the Async Daemon from DocumentStore

All of the samples so far assumed that your application used the `AddMarten()` extension
methods to configure Marten in an application bootstrapped by `IHostBuilder`. If instead you
want to use the async daemon from just an `IDocumentStore`, here's how you do it:

<!-- snippet: sample_use_async_daemon_alone -->
<a id='snippet-sample_use_async_daemon_alone'></a>
```cs
public static async Task UseAsyncDaemon(IDocumentStore store, CancellationToken cancellation)
{
    using var daemon = await store.BuildProjectionDaemonAsync();

    // Fire up everything!
    await daemon.StartAllAsync();

    // or instead, rebuild a single projection
    await daemon.RebuildProjectionAsync("a projection name", 5.Minutes(), cancellation);

    // or a single projection by its type
    await daemon.RebuildProjectionAsync<TripProjectionWithCustomName>(5.Minutes(), cancellation);

    // Be careful with this. Wait until the async daemon has completely
    // caught up with the currently known high water mark
    await daemon.WaitForNonStaleData(5.Minutes());

    // Start a single projection shard
    await daemon.StartAgentAsync("shard name", cancellation);

    // Or change your mind and stop the shard you just started
    await daemon.StopAgentAsync("shard name");

    // No, shut them all down!
    await daemon.StopAllAsync();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L135-L165' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_async_daemon_alone' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Open Telemetry and Metrics <Badge type="tip" text="7.10" />

::: info
All of these facilities are used automatically by Marten. 
:::

See [Open Telemetry and Metrics](/otel) to learn more about exporting Open Telemetry data and metrics
from systems using Marten. 

If your system is configured to export metrics and Open Telemetry data from Marten like this:

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

*And* you are running the async daemon in your system, you should see potentially activities for each running projection
or subscription with the prefix: `marten.{Subscription or Projection Name}.{shard key, basically always "all" at this point}`:

* `execution` -- traces the execution of a page of events through the projection or subscription, with tags for the tenant id, event sequence floor and ceiling, and database name
* `loading` -- traces the loading of a page of events for a projection or subscription. Same tags as above
* `grouping` -- traces the grouping process for projections that happens prior to execution. This does not apply to subscriptions. Same tags as above

In addition, there are two metrics built for every combination of projection or subscription shard on each
Marten database (in the case of using separate databases for multi-tenancy), again using the same prefix as above
with the addition of the Marten database identifier in the case of multi-tenancy through separate databases like `marten.{database name}.{projection or subscription name}.all.*:

* `processed` - a counter giving you an indication of how many events are being processed by the currently running subscription or projection shard
* `gap` - a histogram telling you the "gap" between the high water mark of the system and the furthest progression of the running subscription or projection. 

::: tip
The `gap` metrics are a good health check on the performance of any given projection or subscription. If this gap
is growing, that's a sign that your projection or subscription isn't being able to keep up with the incoming
events
:::

## High Water Mark <Badge type="tip" text="7.33" />

One of the possible issues in Marten operation is "event skipping" in the async daemon where the high water mark
detection grows "stale" because of gaps in the event sequence (generally caused by either very slow outstanding transactions or errors)
and Marten emits an error message like this in the log file:

```js
"High Water agent is stale after threshold of {DelayInSeconds} seconds, skipping gap to events marked after {SafeHarborTime} for database {Name}"
```

With the recent prevalence of [Open Telemetry](https://opentelemetry.io/) tooling in the software industry, Marten
is now emitting Open Telemetry spans and metrics around the high water mark detection in the async daemon.

First off, Marten is emitting spans named either `marten.daemon.highwatermark` in the case of
only targeting a single database, or `marten.[database name].daemon.highwatermark` in the case of 
using multi-tenancy through a database per tenant. On these spans will be these tags:

* `sequence` -- the largest event sequence that has been assigned to the database at this point
* `status` -- either `CaughtUp`, `Changed`, or `Stale` meaning "all good", "proceeding normally", or "uh, oh, something is up with outstanding transactions"
* `current.mark` -- the current, detected "high water mark" where Marten says is the ceiling on where events can be safely processed
* `skipped` -- this tag will only be present as a "true" value if Marten is forcing the high water detection to skip stale gaps in the event sequence
* `last.mark` -- if skipping event sequences, this will be the last good mark before the high water detection calculated the skip

There is also a counter metric called `marten.daemon.skipping` or `marten.[database name].daemon.skipping`
that just emits and update every time that Marten has to "skip" stale events.

## Querying for Non Stale Data

There are some potential benefits to running projections asynchronously, namely:

* Avoiding concurrent updates to aggregated documents so that the results are accurate, especially when the aggregation is "multi-stream"
* Putting the work of building aggregates into a background process so you don't take the performance "hit" of doing that work during requests from a client

All that being said, using asynchronous projections means you're going into the realm of [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency), and sometimes
that's really inconvenient when your users or clients expect up to date information about the projected aggregate data. 

Not to worry though, because Marten will allow you to "wait" for an asynchronous projection to catch up so that you
can query the latest information as all the events captured at the time of the query are processed through the asynchronous
projection like so:

<!-- snippet: sample_using_query_for_non_stale_data -->
<a id='snippet-sample_using_query_for_non_stale_data'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));
    opts.Projections.Add<TripProjection>(ProjectionLifecycle.Async);
}).AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();

// DocumentStore() is an extension method in Marten just
// as a convenience method for test automation
await using var session = host.DocumentStore().LightweightSession();

// This query operation will first "wait" for the asynchronous projection building the
// Trip aggregate document to catch up to at least the highest event sequence number assigned
// at the time this method is called
var latest = await session.QueryForNonStaleData<Trip>(5.Seconds())
    .OrderByDescending(x => x.Started)
    .Take(10)
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/querying_with_non_stale_data.cs#L133-L157' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_query_for_non_stale_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that this can time out if the projection just can't catch up to the latest event sequence in time. You may need to
be both cautious with using this in general, and also cautious especially with the timeout setting. 
