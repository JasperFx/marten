using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// #5652: a store with no event types and no projections never has its event tables migrated, so
/// reading the max event sequence for a usage descriptor asked Postgres for a table that is not
/// there. Marten swallowed the failure, but Postgres logged an ERROR for every attempt, and under
/// Wolverine-managed subscription distribution the leader builds a descriptor for every store on a
/// ten second heartbeat -- tens of thousands of ERROR lines a day for a store that can never hold
/// an event.
/// </summary>
public class inactive_event_store_descriptors
{
    [Fact]
    public async Task no_event_table_read_is_attempted_for_a_store_with_no_event_types()
    {
        var logger = new RecordingLogger();

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "inactive_event_store_5652";
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.DotNetLogger = logger;
            // deliberately no Events.AddEventType and no projections
        });

        var usage = await ((IEventStore)store).TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.MaxEventSequence.ShouldBeNull();

        // The read is reported from the catch block that makes it survivable, so its absence is
        // what says no failing statement reached Postgres in the first place.
        logger.Messages.ShouldNotContain(x => x.Contains("Event storage backing"));
    }

    /// <summary>
    /// The guard must not cost a store that does have events its answer -- the whole reason #5511
    /// settled on asking Postgres rather than inferring from configuration.
    /// </summary>
    [Fact]
    public async Task a_store_that_does_have_events_still_reports_its_max_sequence()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "active_event_store_5652";
            opts.Events.AddEventType<QuestStarted>();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new QuestStarted { Name = "trip-1" });
            await session.SaveChangesAsync();
        }

        var usage = await ((IEventStore)store).TryCreateUsage(CancellationToken.None);

        usage.ShouldNotBeNull();
        usage.MaxEventSequence.ShouldNotBeNull();
        usage.MaxEventSequence.Value.ShouldBeGreaterThan(0);
    }

    private class RecordingLogger: ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception));
        }

        private class NullScope: IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
