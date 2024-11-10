using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using EventSourcingTests.Projections;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Services;
using Marten.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Weasel.Core.Operations;

namespace DaemonTests.Subscriptions;

#region sample_ConsoleSubscription

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

#endregion

#region sample_ErrorHandlingSubscription

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

#endregion

public static class SubscriptionBootstrapping
{
    public static async Task bootstrap_console()
    {
        #region sample_register_ConsoleSubscription

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

        #endregion
    }

    public static async Task bootstrap_kafka()
    {
        #region sample_registering_KafkaSubscription

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

        #endregion
    }

    public static async Task specifying_starting_position()
    {
        #region sample_starting_position_of_subscription

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

        #endregion
    }

    #region sample_rewinding_subscriptions

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

    #endregion

    public static async Task specifying_event_filters()
    {
        #region sample_subscription_filters

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

        #endregion
    }
}

#region sample_KafkaSubscription

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

#endregion
