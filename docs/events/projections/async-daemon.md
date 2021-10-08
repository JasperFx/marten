# Async Projections Daemon

::: tip
The *async daemon* subsystem was completely rewritten in Marten V4. As of right now, this is the only "in the box" solution
for asynchronous projection support. The hope and plan is for Marten to grow into other alternatives based around data streaming tools
like Kafka, but this has not been determined or started yet.
:::

For more information, see Jeremy's blog post on [Offline Event Processing in Marten with the new “Async Daemon”](https://jeremydmiller.com/2016/08/04/offline-event-processing-in-marten-with-the-new-async-daemon/).

The *Async Daemon* is the nickname for Marten's built in asynchronous projection processing engine. The current async daemon from Marten V4 on requires no other infrastructure
besides Postgresql and Marten itself. The daemon itself runs inside an [IHostedService](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-5.0&tabs=visual-studio) implementation in your application. The **daemon is disabled by default**.

The *Async Daemon* will process events **in order** through all projections registered with an
asynchronous lifecycle. 

First, some terminology:

* *Projection* -- a projected view defined by the `IProjection` interface and registered with Marten. See also [Projections](/events/projections/).
* *Projection Shard* -- a logical segment of events that are executed separately by the async daemon
* *High Water Mark* -- the furthest known event sequence that the daemon "knows" that all events with 
  that sequence or lower can be safely processed in order by projections. The high water mark will
  frequently be a little behind the highest known event sequence number if outstanding gaps
  in the event sequence are detected. 


There are only two basic things to configure the *Async Daemon*:

1. Register the projections that should run asynchronously
1. Set the `StoreOptions.AsyncMode` to either `Solo` or `HotCold` (more on what these options mean later in this page)

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

            // Turn on the async daemon in "Solo" mode
            opts.Projections.AsyncMode = DaemonMode.Solo;

            // Register any projections you need to run asynchronously
            opts.Projections.Add<TripAggregationWithCustomName>(ProjectionLifecycle.Async);
        });
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L20-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrap_daemon_solo' title='Start of snippet'>anchor</a></sup>
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

            // Turn on the async daemon in "HotCold" mode
            // with built in leader election
            opts.Projections.AsyncMode = DaemonMode.HotCold;

            // Register any projections you need to run asynchronously
            opts.Projections.Add<TripAggregationWithCustomName>(ProjectionLifecycle.Async);
        });
    })
    .StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L99-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_bootstrap_daemon_hotcold' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Solo vs. HotCold

::: tip
Marten's leader election is done with Postgresql advisory locks, so there is no additional software infrastructure necessary other than
Postgresql and Marten itself.
:::

As of right now, the daemon can run as one of two modes:

1. *Solo* -- the daemon will be automatically started when the application is bootstrapped and all projections and projection shards will be started on that node. The assumption with Solo
   is that there is never more than one running system node for your application.
1. *HotCold* -- the daemon will use a built in [leader election](https://en.wikipedia.org/wiki/Leader_election) function to guarantee that the daemon is only running on
   one active node
   
Regardless of how things are configured, the daemon is designed to detect when multiple running processes are updating the same projection shard and will shut down 
the process if concurrency issues persist. 

## Daemon Logging

The daemon logs through the standard .Net `ILogger` interface service registered in your application's underlying DI container.

## Error Handling

**In all examples, `opts` is a `StoreOptions` object.

The error handling in the daemon is configurable so that you can fine tune how the daemon handles
exceptions that it encounters. There is a fluent interface API off of `StoreOptions.Projections` that
will allow you to specify exception filters by a combination of exception type and/or a user supplied
Lambda filter and an action to take when that event is encountered.

The possible actions are to:

* Retry the action that caused the exception immediately
* Retry the action that caused the exception later after a configurable amount of time
* Stop the current projection shard
* Stop all the projection shards running on the current node
* Pause the current projection shard for a user supplied duration of time
* Pause all the running projection shards for a user supplied duration of time
* Do nothing and pretend nothing is wrong -- but you probably shouldn't be opting for this very often

For example, you can make the daemon stop a projection anytime a
projection encounters an `InvalidOperationException` like so:

<!-- snippet: sample_stop_shard_on_exception -->
<a id='snippet-sample_stop_shard_on_exception'></a>
```cs
// Stop only the current exception
opts.Projections.OnException<InvalidOperationException>()
    .Stop();

// or get more granular
opts.Projections
    .OnException<InvalidOperationException>(e => e.Message.Contains("Really bad!"))

    .Stop(); // stops just the current projection shard
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L59-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stop_shard_on_exception' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For retry mechanics, you can specify a finite number of retries, then chain
an additional action as shown below in this [exponential back-off error handling strategy](https://en.wikipedia.org/wiki/Exponential_back-off):

<!-- snippet: sample_exponential_back-off_strategy -->
<a id='snippet-sample_exponential_back-off_strategy'></a>
```cs
opts.Projections.OnException<NpgsqlException>()
    .RetryLater(50.Milliseconds(), 250.Milliseconds(), 500.Milliseconds())
    .Then
    .Pause(1.Minutes());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L81-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_exponential_back-off_strategy' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Default Error Handling Policies

Here is the default error handling policies in the daemon:

<!-- snippet: sample_default_daemon_exception_policies -->
<a id='snippet-sample_default_daemon_exception_policies'></a>
```cs
OnException<EventFetcherException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
    .Then.Pause(30.Seconds());

OnException<ShardStopException>().DoNothing();

OnException<ShardStartException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
    .Then.DoNothing();

OnException<NpgsqlException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
    .Then.Pause(30.Seconds());

OnException<MartenCommandException>().RetryLater(250.Milliseconds(), 500.Milliseconds(), 1.Seconds())
    .Then.Pause(30.Seconds());

// This exception means that the daemon has detected that another process
// has updated the current projection shard. When this happens, Marten will stop
// and restart the projection from its last known "good" point in 10 seconds
OnException<ProgressionProgressOutOfOrderException>().Pause(10.Seconds());
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Daemon/DaemonSettings.cs#L50-L71' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_default_daemon_exception_policies' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Any user supplied policies would take precedence over the default policies.

## Poison Event Detection

Occasionally there might be some kind of error applying a specific event in an asynchronous projection
where the event data itself is problematic. In this case, you may want to treat it as a *poison pill message*
and teach the daemon to ignore that event and continue without it in the sequence. 

Here's an example of teaching the daemon to ignore and skip events that encounter a certain type of exception:

<!-- snippet: sample_poison_pill -->
<a id='snippet-sample_poison_pill'></a>
```cs
opts.Projections.OnApplyEventException()
    .AndInner<ArithmeticException>()
    .SkipEvent();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L73-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_poison_pill' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L124-L146' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_daemondiagnostics' title='Start of snippet'>anchor</a></sup>
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

And lastly, to rebuild a single projection at a time, use:

```bash
dotnet run -- projections --rebuild -p [shard name]
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
    using var daemon = store.BuildProjectionDaemon();

    // Fire up everything!
    await daemon.StartAllShards();

    // or instead, rebuild a single projection
    await daemon.RebuildProjection("a projection name", cancellation);

    // or a single projection by its type
    await daemon.RebuildProjection<TripAggregationWithCustomName>(cancellation);

    // Be careful with this. Wait until the async daemon has completely
    // caught up with the currently known high water mark
    await daemon.WaitForNonStaleData(5.Minutes());

    // Start a single projection shard
    await daemon.StartShard("shard name", cancellation);

    // Or change your mind and stop the shard you just started
    await daemon.StopShard("shard name");

    // No, shut them all down!
    await daemon.StopAll();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CommandLineRunner/AsyncDaemonBootstrappingSamples.cs#L148-L178' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_use_async_daemon_alone' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


