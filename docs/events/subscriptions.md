# Event Subscriptions <Badge type="tip" text="7.7" />

::: tip
The new subscription model is leaner, and more efficient for background work than using the `IProjection` model that
does a lot of preprocessing and grouping that is not necessary or always desirable for subscriptions anyway. 
:::

The existing projections model in Marten has a world of recipes for "projecting" Marten event storage into read-only
views of the event data, but what if you need to carry out some kind of background processing on these events as they
are captured? For example, maybe you need to:

* Publish events to an external system as some kind of integration?
* Carry out background processing based on a captured event 
* Build a view representation of the events in something outside of the current PostgreSQL database, like maybe an Elastic
  Search view for better searching

In previous versions of Marten, you had to utilize the `IProjection` interface as a mechanism for integrating Marten
events to other systems or just for conducting background processing on published events as shown in the blog post [Integrating Marten with other systems](https://event-driven.io/en/integrating_Marten/).
Now though, you can also utilize Marten's `ISubscription` model that runs within Marten's [async daemon subsystem](/events/projections/async-daemon)
to "push" events into your subscriptions as events flow into your system. **Note that this is a background process
within your application, and happen in a completely different thread than the initial work of appending and saving
events to the Marten event storage.**

![Marten's Subscription Model](/images/subscriptions.png)

Subscriptions will always be an implementation of the `ISubscription` interface shown below:

<!-- snippet: sample_ISubscription -->
<a id='snippet-sample_isubscription'></a>
```cs
/// <summary>
/// Basic abstraction for custom subscriptions to Marten events through the async daemon. Use this in
/// order to do custom processing against an ordered stream of the events
/// </summary>
public interface ISubscription : IAsyncDisposable
{
    /// <summary>
    /// Processes a page of events at a time
    /// </summary>
    /// <param name="page"></param>
    /// <param name="controller">Use to log dead letter events that are skipped or to stop the subscription from processing based on an exception</param>
    /// <param name="operations">Access to Marten queries and writes that will be committed with the progress update for this subscription</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Subscriptions/ISubscription.cs#L9-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_isubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

So far, the subscription model gives you these abilities:

* Access to the Marten `IDocumentOperations` service that is scoped to the processing of a single page and can
  be used to either query additional data or to make database writes within the context of the same transaction that
  Marten will use to record the current progress of the subscription to the databsae
* Error handling abilities via the `ISubscriptionController` interface argument that can be used to record events
  that were skipped by the subscription or to completely stop all further processing
* By returning an `IChangeListener`, the subscription can be notified right before and right after Marten commits
  the database transaction for any changes including recording the current progress of the subscription for the
  current page. This was done purposely to enable transactional outbox approaches like the one in [Wolverine](https://wolverinefx.net). See
  [the async daemon diagnostics](/diagnostics.html#listening-for-async-daemon-events) for more information. 
* The ability to filter the event types or stream types that the subscription is interested in as a way to greatly
  optimize the runtime performance by preventing Marten from having to fetch events that the subscription will not
  process
* The ability to create the actual subscription objects from the application's IoC container when that is necessary
* Flexible control over *where* or *when* the subscription starts when it is first applied to an existing event store
* Some facility to "rewind and replay" subscriptions

To make this concrete, here's the simplest possible subscription you can make to simply write out a console message
for every event:

<!-- snippet: sample_ConsoleSubscription -->
<a id='snippet-sample_consolesubscription'></a>
```cs
public class ConsoleSubscription: ISubscription
{
    public Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller, IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Starting to process events from {page.SequenceFloor} to {page.SequenceCeiling}");
        foreach (var e in page.Events)
        {
            Console.WriteLine($"Got event of type {e.Data.GetType().NameInCode()} from stream {e.StreamId}");
        }

        // If you don't care about being signaled for
        return Task.FromResult(NullChangeListener.Instance);
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L22-L45' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_consolesubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And to register that with our Marten store:

<!-- snippet: sample_register_ConsoleSubscription -->
<a id='snippet-sample_register_consolesubscription'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));

        // Because this subscription has no service dependencies, we
        // can use this simple mechanism
        opts.Events.Subscribe(new ConsoleSubscription());

        // Or with additional configuration like:
        opts.Events.Subscribe(new ConsoleSubscription(), s =>
        {
            s.SubscriptionName = "Console"; // Override Marten's naming
            s.SubscriptionVersion = 2; // Potentially version as an all new subscription

            // Optionally create an allow list of
            // event types to subscribe to
            s.IncludeType<InvoiceApproved>();
            s.IncludeType<InvoiceCreated>();

            // Only subscribe to new events, and don't try
            // to apply this subscription to existing events
            s.Options.SubscribeFromPresent();
        });
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L130-L162' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_register_consolesubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Here's a slightly more complicated sample that publishes events to a configured Kafka topic:

<!-- snippet: sample_KafkaSubscription -->
<a id='snippet-sample_kafkasubscription'></a>
```cs
public class KafkaSubscription: SubscriptionBase
{
    private readonly KafkaProducerConfig _config;

    public KafkaSubscription(KafkaProducerConfig config)
    {
        _config = config;

        SubscriptionName = "Kafka";

        // Access to any or all filtering rules
        IncludeType<InvoiceApproved>();

        // Fine grained control over how the subscription runs
        // in the async daemon
        Options.BatchSize = 1000;
        Options.MaximumHopperSize = 10000;

        // Effectively run as a hot observable
        Options.SubscribeFromPresent();
    }

    // The daemon will "push" a page of events at a time to this subscription
    public override async Task<IChangeListener> ProcessEventsAsync(
        EventRange page,
        ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        using var kafkaProducer =
            new ProducerBuilder<string, string>(_config.ProducerConfig).Build();

        foreach (var @event in page.Events)
        {
            await kafkaProducer.ProduceAsync(_config.Topic,
                new Message<string, string>
                {
                    // store event type name in message Key
                    Key = @event.Data.GetType().Name,
                    // serialize event to message Value
                    Value = JsonConvert.SerializeObject(@event.Data)
                }, cancellationToken);

        }

        // We don't need any kind of callback, so the nullo is fine
        return NullChangeListener.Instance;
    }

}

// Just assume this is registered in your IoC container
public class KafkaProducerConfig
{
    public ProducerConfig? ProducerConfig { get; set; }
    public string? Topic { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L288-L348' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_kafkasubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This time, it's requiring IoC services injected through its constructor, so we're going to use this mechanism
to add it to Marten:

<!-- snippet: sample_registering_KafkaSubscription -->
<a id='snippet-sample_registering_kafkasubscription'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));
    })
    // Marten also supports a Scoped lifecycle, and quietly forward Transient
    // to Scoped
    .AddSubscriptionWithServices<KafkaSubscription>(ServiceLifetime.Singleton, o =>
    {
        // This is a default, but just showing what's possible
        o.IncludeArchivedEvents = false;

        o.FilterIncomingEventsOnStreamType(typeof(Invoice));

        // Process no more than 10 events at a time
        o.Options.BatchSize = 10;
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L167-L191' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_kafkasubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Registering Subscriptions

::: info
Marten can support both the `Singleton` and `Scoped` lifetimes when using the IoC container to build out your
subscription. If you specify `Transient`, Marten will still use the `Scoped` lifetime.
:::

Stateless subscriptions can simply be registered like this:

<!-- snippet: sample_register_ConsoleSubscription -->
<a id='snippet-sample_register_consolesubscription'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));

        // Because this subscription has no service dependencies, we
        // can use this simple mechanism
        opts.Events.Subscribe(new ConsoleSubscription());

        // Or with additional configuration like:
        opts.Events.Subscribe(new ConsoleSubscription(), s =>
        {
            s.SubscriptionName = "Console"; // Override Marten's naming
            s.SubscriptionVersion = 2; // Potentially version as an all new subscription

            // Optionally create an allow list of
            // event types to subscribe to
            s.IncludeType<InvoiceApproved>();
            s.IncludeType<InvoiceCreated>();

            // Only subscribe to new events, and don't try
            // to apply this subscription to existing events
            s.Options.SubscribeFromPresent();
        });
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L130-L162' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_register_consolesubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

But, if you need to utilize services from your IoC container within your subscription -- and you very likely do --
you can utilize the `AddSubscriptionWithServices()` mechanisms:

<!-- snippet: sample_registering_KafkaSubscription -->
<a id='snippet-sample_registering_kafkasubscription'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));
    })
    // Marten also supports a Scoped lifecycle, and quietly forward Transient
    // to Scoped
    .AddSubscriptionWithServices<KafkaSubscription>(ServiceLifetime.Singleton, o =>
    {
        // This is a default, but just showing what's possible
        o.IncludeArchivedEvents = false;

        o.FilterIncomingEventsOnStreamType(typeof(Invoice));

        // Process no more than 10 events at a time
        o.Options.BatchSize = 10;
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L167-L191' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_kafkasubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Starting Position of Subscriptions

By default, a registered subscription will be started at the very beginning of the event store (but does ignore archived events by default).
That's not always going to be a good default, so Marten gives you the ability to specify the starting point of a subscription
when a brand new subscription with no existing progress is started for the first time:

<!-- snippet: sample_starting_position_of_subscription -->
<a id='snippet-sample_starting_position_of_subscription'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));
    })
    // Marten also supports a Scoped lifecycle, and quietly forward Transient
    // to Scoped
    .AddSubscriptionWithServices<KafkaSubscription>(ServiceLifetime.Singleton, o =>
    {
        // Start the subscription at the most current "high water mark" of the
        // event store. This effectively makes the subscription a "hot"
        // observable that only sees events when the subscription is active
        o.Options.SubscribeFromPresent();

        // Only process events in the store from a specified event sequence number
        o.Options.SubscribeFromSequence(1000);

        // Only process events in the store by determining the floor by the event
        // timestamp information
        o.Options.SubscribeFromTime(new DateTimeOffset(2024, 4, 1, 0, 0, 0, 0.Seconds()));

        // All of these options can be explicitly applied to only a single
        // named database when using multi-tenancy through separate databases
        o.Options.SubscribeFromPresent("Database1");
        o.Options.SubscribeFromSequence(2000, "Database2");
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L196-L229' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_starting_position_of_subscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you specify starting rules for a certain database, that rule will only apply to that database. Other databases will
fall through to global rules.

## Event Filtering

Without any explicit configuration, all subscriptions will receive all possible event types, but Marten will filter
out events marked as archived. If your subscription only cares about a subset of the possible event types in your application,
there's a big performance advantage to filtering the event types for your subscription by effectively creating an 
allow list of allowable event types or stream types. The following is an example:

<!-- snippet: sample_subscription_filters -->
<a id='snippet-sample_subscription_filters'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));
    })
    // Marten also supports a Scoped lifecycle, and quietly forward Transient
    // to Scoped
    .AddSubscriptionWithServices<KafkaSubscription>(ServiceLifetime.Singleton, o =>
    {
        // Archived events are ignored by default, but you can override that here
        o.IncludeArchivedEvents = true;

        // If you use more than one type of stream type marker, it might
        // be quick step to just include any events from a stream marked
        // as the "Invoice" type
        o.FilterIncomingEventsOnStreamType(typeof(Invoice));

        // Or be explicit about the specific event types
        // NOTE: you need to use concrete types here
        o.IncludeType<InvoiceCreated>();
        o.IncludeType<InvoiceApproved>();
    })
    .AddAsyncDaemon(DaemonMode.HotCold);

using var host = builder.Build();
await host.StartAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L255-L284' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_subscription_filters' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Note that all filters are applied with a SQL `OR` operator in the underlying data fetching. 

## Using SubscriptionBase

The `SubscriptionBase` class can be used as a convenient base class for subscriptions that also serves to embed all
the various configuration options for that subscription right into the subscription itself. The usage of that
base class is shown below:

<!-- snippet: sample_KafkaSubscription -->
<a id='snippet-sample_kafkasubscription'></a>
```cs
public class KafkaSubscription: SubscriptionBase
{
    private readonly KafkaProducerConfig _config;

    public KafkaSubscription(KafkaProducerConfig config)
    {
        _config = config;

        SubscriptionName = "Kafka";

        // Access to any or all filtering rules
        IncludeType<InvoiceApproved>();

        // Fine grained control over how the subscription runs
        // in the async daemon
        Options.BatchSize = 1000;
        Options.MaximumHopperSize = 10000;

        // Effectively run as a hot observable
        Options.SubscribeFromPresent();
    }

    // The daemon will "push" a page of events at a time to this subscription
    public override async Task<IChangeListener> ProcessEventsAsync(
        EventRange page,
        ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        using var kafkaProducer =
            new ProducerBuilder<string, string>(_config.ProducerConfig).Build();

        foreach (var @event in page.Events)
        {
            await kafkaProducer.ProduceAsync(_config.Topic,
                new Message<string, string>
                {
                    // store event type name in message Key
                    Key = @event.Data.GetType().Name,
                    // serialize event to message Value
                    Value = JsonConvert.SerializeObject(@event.Data)
                }, cancellationToken);

        }

        // We don't need any kind of callback, so the nullo is fine
        return NullChangeListener.Instance;
    }

}

// Just assume this is registered in your IoC container
public class KafkaProducerConfig
{
    public ProducerConfig? ProducerConfig { get; set; }
    public string? Topic { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L288-L348' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_kafkasubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Rewinding or Replaying Subscriptions

::: info
There are plans for a commercial add on to Marten to expose this functionality through a user interface control panel
:::

Marten today has a limited ability to rewind a subscription to a certain point, then restart it to run
continuously from there on. Note that this only works today within a single process. Here's a sample of 
doing this operation:

<!-- snippet: sample_rewinding_subscriptions -->
<a id='snippet-sample_rewinding_subscriptions'></a>
```cs
// IProjectionCoordinator is a service from Marten that's added to your IoC
// container and gives you access to the running async daemon instance in
// your process
public static async Task rewinding_subscription(IProjectionCoordinator coordinator)
{
    var daemon = coordinator.DaemonForMainDatabase();

    // Rewind and restart the named subscription at sequence 0
    await daemon.RewindSubscriptionAsync("Kafka",  CancellationToken.None);

    // Rewind and restart the named subscription at sequence 2000
    await daemon.RewindSubscriptionAsync("Kafka",  CancellationToken.None, sequenceFloor:2000);

    // Rewind and restart the named subscription for the events after a certain time
    await daemon.RewindSubscriptionAsync("Kafka",  CancellationToken.None, timestamp:DateTimeOffset.UtcNow.Subtract(1.Days()));
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L232-L251' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_rewinding_subscriptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Error Handling

::: warning
If you allow an exception to bubble out of the `ProcessEventsAsync()` method in your subscription, Marten
will treat that as a critical exception that will rollback the ongoing work and pause the subscription. The 
subscription will be "rewound" to its previous position when Marten tries to restart the subscription. 
:::

As the author of a subscription, you should strive to handle exceptions internally within the 
subscription itself as much as possible. You do have the ability to use the `ISubscriptionController`
argument to record "dead letter events" that are skipped internally with an exception or 
to signal to Marten when a subscription has to be stopped partway throught the current page. Doing this
will allow the previous work to go forward, but the subscription will be paused afterward at the point
that the controller is told. 

The following is an example of using these facilities for error handling:

<!-- snippet: sample_ErrorHandlingSubscription -->
<a id='snippet-sample_errorhandlingsubscription'></a>
```cs
public class ErrorHandlingSubscription: SubscriptionBase
{
    public override async Task<IChangeListener> ProcessEventsAsync(
        // The current "page" of events in strict sequential order
        // If using conjoined tenancy, this will be a mix of tenants!
        EventRange page,
        ISubscriptionController controller,

        // This gives you access to make "writes" to the
        // underlying Marten store
        IDocumentOperations operations,
        CancellationToken cancellationToken)
    {

        long lastProcessed = page.SequenceFloor;

        // Do any processing of events you want here
        foreach (var e in page.Events)
        {
            Console.WriteLine($"Got event of type {e.Data.GetType().NameInCode()} from stream {e.StreamId}");

            try
            {
                await handleEvent(e);
                lastProcessed = e.Sequence;
            }
            catch (ReallyBadException ex)
            {
                // We've detected some kind of critical exception that makes us
                // want to stop all further processing
                await controller.ReportCriticalFailureAsync(ex, lastProcessed);
            }
            catch (Exception ex)
            {
                // Not great, but hey, we can skip this and keep going!
                await controller.RecordDeadLetterEventAsync(e, ex);
            }
        }

        // This is a mechanism for subscriptions to "know" when the progress for a page of events and any
        // pending writes to the Marten store are about to be committed or have just been committed
        // This was added specifically to enable Wolverine integration with its transactional outbox
        return new Callback();
    }

    private async Task handleEvent(IEvent @event)
    {
        // do some custom work on this event
    }

    // This is a mechanism to allow the subscription to "know" when Marten is about to persist
    internal class Callback: IChangeListener
    {
        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            Console.WriteLine("Marten is about to make a commit for any changes");
            return Task.CompletedTask;
        }

        public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
        {
            Console.WriteLine("Marten just made a commit for any changes");
            return Task.CompletedTask;
        }
    }
}

public class ReallyBadException: Exception
{
    public ReallyBadException(string message) : base(message)
    {
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AsyncDaemon.Testing/Subscriptions/SubscriptionSamples.cs#L47-L124' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_errorhandlingsubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->






