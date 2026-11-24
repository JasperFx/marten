#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;

namespace EventSourcingTests.Examples;

// Documentation samples for the event store API pages: docs/events/rewriting.md,
// docs/events/versioning.md, docs/events/projections/live-aggregates.md,
// docs/scenarios/command_handler_workflow.md, docs/events/projections/side-effects.md and
// docs/events/projections/rebuilding.md. These compile, but are not tests.

public record ApiDocsTicketOpened(string Title, string ReporterEmail);

public record ApiDocsTicketAssigned(string Assignee);

public record ApiDocsTicketClosed(string Resolution);

public record ApiDocsTicketPurged;

public class ApiDocsTicket
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Assignee { get; set; }
    public bool IsClosed { get; set; }

    public static ApiDocsTicket Create(ApiDocsTicketOpened e) => new() { Title = e.Title };

    public void Apply(ApiDocsTicketAssigned e) => Assignee = e.Assignee;

    public void Apply(ApiDocsTicketClosed e) => IsClosed = true;

    // Live aggregation returns null once the stream holds this event
    public bool ShouldDelete(ApiDocsTicketPurged e) => true;
}

public record ApiDocsCloseTicket(Guid TicketId, string Resolution);

public record ApiDocsPaymentReceived(Guid AccountId, decimal Amount);

public class ApiDocsAccountBalance
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
}

public partial class ApiDocsAccountBalanceProjection: MultiStreamProjection<ApiDocsAccountBalance, Guid>
{
    public ApiDocsAccountBalanceProjection()
    {
        Identity<ApiDocsPaymentReceived>(e => e.AccountId);
    }

    public void Apply(ApiDocsPaymentReceived e, ApiDocsAccountBalance balance) => balance.Total += e.Amount;
}

public static class ApiDocsV1
{
    public record OrderPlaced(Guid OrderId, decimal Total);
}

public static class ApiDocsV2
{
    public record OrderPlaced(Guid OrderId, decimal Total, string Currency);
}

public record ApiDocsOrderConfirmed(Guid OrderId);

public record ApiDocsOrderShipped(Guid OrderId);

public class ApiDocsOrderLine
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
}

#region sample_subscribe_from_prior_version_mark

public partial class ApiDocsOrderLineProjection: EventProjection
{
    public ApiDocsOrderLineProjection()
    {
        // Version 1 of this projection has been running in production
        Version = 2;

        // Start version 2 at the sequence version 1 had reached, instead of replaying
        // the whole event store
        Options.SubscribeFromPriorVersionMark("order-lines-v2");
    }

    public void Project(ApiDocsV2.OrderPlaced e, IDocumentOperations ops)
        => ops.Store(new ApiDocsOrderLine { Id = e.OrderId, Total = e.Total, Currency = e.Currency });
}

#endregion

#region sample_custom_message_outbox

public class ApiDocsMessageOutbox: IMessageOutbox
{
    public ValueTask<IMessageBatch> CreateBatch(DocumentSessionBase session)
        => new(new ApiDocsMessageBatch());
}

public class ApiDocsMessageBatch: IMessageBatch
{
    // PublishAsync() is called from several threads at once by the async daemon
    private readonly ConcurrentQueue<(object Message, string TenantId)> _messages = new();

    public ValueTask PublishAsync<T>(T message, string tenantId)
    {
        _messages.Enqueue((message!, tenantId));
        return ValueTask.CompletedTask;
    }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // Runs inside Marten's transaction, immediately before the COMMIT. Write the
        // messages to an outbox table here to store them with the projection's changes
        return Task.CompletedTask;
    }

    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // Runs after the commit. Send the messages, or tell the outbox they are ready
        return Task.CompletedTask;
    }
}

#endregion

public static class EventStoreApiDocsSamples
{
    public static async Task overwrite_event(IDocumentSession session, Guid ticketId)
    {
        #region sample_overwrite_event

        var events = await session.Events.FetchStreamAsync(ticketId);

        foreach (var opened in events.OfType<Event<ApiDocsTicketOpened>>())
        {
            // Change the body of the event you loaded...
            opened.Data = opened.Data with { ReporterEmail = "redacted" };

            // ...and its headers, which are only written when headers are enabled
            opened.SetHeader("redacted-by", "support");

            session.Events.OverwriteEvent(opened);
        }

        // Nothing is written until SaveChangesAsync()
        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task completely_replace_event(IDocumentSession session, Guid ticketId)
    {
        #region sample_completely_replace_event

        var events = await session.Events.FetchStreamAsync(ticketId);
        var last = events[^1];

        // The replacement can be of a different event type than the original. Marten
        // returns the new id it assigns to the event
        var newEventId = session.Events.CompletelyReplaceEvent(last.Sequence,
            new ApiDocsTicketClosed("Closed by an administrator"));

        await session.SaveChangesAsync();

        #endregion
    }

    public static async Task aggregate_to_last_known(IQuerySession session, Guid ticketId)
    {
        #region sample_aggregate_stream_to_last_known

        // Null if the stream's last event is ApiDocsTicketPurged
        var current = await session.Events.AggregateStreamAsync<ApiDocsTicket>(ticketId);

        // The ticket as it was just before it was purged
        var lastKnown = await session.Events.AggregateStreamToLastKnownAsync<ApiDocsTicket>(ticketId);

        #endregion
    }

    public static void register_balance_projection(StoreOptions opts)
    {
        #region sample_registering_projection_for_aggregate_to_many

        // AggregateToManyAsync<T>() needs a registered aggregate projection for T
        opts.Projections.Add<ApiDocsAccountBalanceProjection>(ProjectionLifecycle.Async);

        #endregion
    }

    public static async Task aggregate_to_many(IQuerySession session, Guid importStream, Guid correctionsStream)
    {
        #region sample_aggregate_to_many_usage

        // Events from any streams, folded by the projection's own grouping (AccountId here)
        // into one ApiDocsAccountBalance per account
        IReadOnlyList<ApiDocsAccountBalance> balances = await session.Events
            .QueryAllRawEvents()
            .Where(x => x.StreamId == importStream || x.StreamId == correctionsStream)
            .AggregateToManyAsync<ApiDocsAccountBalance>();

        #endregion
    }

    #region sample_write_exclusively_to_aggregate

    public static Task Handle(ApiDocsCloseTicket command, IDocumentSession session)
    {
        // Takes a row lock on the stream, runs the lambda, then calls SaveChangesAsync()
        return session.Events.WriteExclusivelyToAggregate<ApiDocsTicket>(command.TicketId, stream =>
        {
            if (stream.Aggregate is { IsClosed: false })
            {
                stream.AppendOne(new ApiDocsTicketClosed(command.Resolution));
            }
        });
    }

    #endregion

    public static void register_message_outbox(StoreOptions opts)
    {
        #region sample_registering_a_message_outbox

        opts.Events.MessageOutbox = new ApiDocsMessageOutbox();

        #endregion
    }

    public static void map_event_type_with_schema_version(StoreOptions opts)
    {
        #region sample_map_event_type_with_schema_version

        opts.Events
            // Events stored before the change are named "order_placed" and are
            // read as the old shape, then upcast
            .Upcast((ApiDocsV1.OrderPlaced old) => new ApiDocsV2.OrderPlaced(old.OrderId, old.Total, "USD"))

            // New ApiDocsV2.OrderPlaced events are stored as "order_placed_v2"
            .MapEventTypeWithSchemaVersion<ApiDocsV2.OrderPlaced>(2);

        #endregion
    }

    public static void map_event_type_with_name_suffix(StoreOptions opts)
    {
        #region sample_map_event_type_with_name_suffix

        opts.Events
            // Stored as "api_docs_order_confirmed_checked"
            .MapEventTypeWithNameSuffix<ApiDocsOrderConfirmed>("checked")

            // Stored as "order_status_changed_shipped"
            .MapEventTypeWithNameSuffix<ApiDocsOrderShipped>("order_status_changed", "shipped");

        #endregion
    }

    public static void map_event_type_with_explicit_name_and_schema_version(StoreOptions opts)
    {
        #region sample_map_event_type_with_explicit_name_and_schema_version

        // Stored as "order_status_changed_v3"
        opts.Events.MapEventTypeWithSchemaVersion<ApiDocsOrderConfirmed>("order_status_changed", 3);

        #endregion
    }
}
