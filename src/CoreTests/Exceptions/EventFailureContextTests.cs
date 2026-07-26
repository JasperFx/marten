using System;
using JasperFx.Events.Daemon;
using Marten.Events;
using Marten.Exceptions;
using Shouldly;
using Xunit;

namespace CoreTests.Exceptions;

// #5048 / jasperfx#565. The daemon deliberately has NO fallback type-name sniffing: a store's exception
// declares its own ShardFailureCategory through IEventFailureContext, or the failure classifies as
// "Other" with no event details. These tests pin that contract on Marten's two read-path exceptions.
public class EventFailureContextTests
{
    public record CorruptedEvent(string Name);

    private static EventMapping<CorruptedEvent> mapping()
    {
        var graph = new EventGraph(new Marten.StoreOptions());
        return (EventMapping<CorruptedEvent>)graph.EventMappingFor<CorruptedEvent>();
    }

    [Fact]
    public void deserialization_failure_declares_its_category_and_names_the_event()
    {
        var eventType = mapping();
        IEventFailureContext exception =
            new EventDeserializationFailureException(4815, eventType, new DivideByZeroException("Boom!"));

        exception.Category.ShouldBe(ShardFailureCategory.EventSerialization);
        exception.Sequence.ShouldBe(4815);

        // The constructor has always been handed the IEventType and used it only to build the message
        // string. Retaining the alias is the point of the change.
        exception.EventTypeName.ShouldBe(eventType.EventTypeName);

        // Raised while reading the row, before there is an IEvent, so nothing else is knowable
        exception.EventId.ShouldBeNull();
        exception.StreamId.ShouldBeNull();
        exception.StreamKey.ShouldBeNull();
        exception.TenantId.ShouldBeNull();
        exception.Version.ShouldBeNull();
    }

    [Fact]
    public void unknown_event_type_is_a_separate_category_from_serialization()
    {
        // A missing registration is a deployment fix, not a data fix, so it must not classify as
        // EventSerialization.
        IEventFailureContext exception = new UnknownEventTypeException("trip_started", 1623);

        exception.Category.ShouldBe(ShardFailureCategory.UnknownEventType);
        exception.Sequence.ShouldBe(1623);
        exception.EventTypeName.ShouldBe("trip_started");
    }

    [Fact]
    public void unknown_event_type_reports_an_unknown_sequence_when_the_throw_site_has_no_row()
    {
        IEventFailureContext exception = new UnknownEventTypeException("trip_started");

        exception.Sequence.ShouldBe(UnknownEventTypeException.UnknownSequence);
    }

    [Fact]
    public void shard_failure_classifies_a_deserialization_failure_through_wrapping()
    {
        var eventType = mapping();
        var inner = new EventDeserializationFailureException(99, eventType, new DivideByZeroException("Boom!"));

        // The per-event exception routinely reaches the daemon wrapped -- ShardStopException around it,
        // or an AggregateException of a whole batch's failures. ShardFailure.For walks the entire graph.
        var wrapped = new AggregateException(new InvalidOperationException("unrelated", new Exception("leaf")),
            new ShardStopException("Trip:All", inner));

        var occurredAt = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
        var failure = ShardFailure.For(wrapped, occurredAt);

        failure.Category.ShouldBe(ShardFailureCategory.EventSerialization);
        failure.Event.ShouldNotBeNull();
        failure.Event.Sequence.ShouldBe(99);
        failure.Event.EventTypeName.ShouldBe(eventType.EventTypeName);
        failure.OccurredAt.ShouldBe(occurredAt);
    }

    [Fact]
    public void shard_failure_classifies_an_unknown_event_type_through_wrapping()
    {
        var failure = ShardFailure.For(
            new ShardStopException("Trip:All", new UnknownEventTypeException("trip_started", 77)),
            DateTimeOffset.UtcNow);

        failure.Category.ShouldBe(ShardFailureCategory.UnknownEventType);
        failure.Event!.Sequence.ShouldBe(77);
        failure.Event.EventTypeName.ShouldBe("trip_started");
    }

    [Fact]
    public void dead_letter_event_id_is_assigned_before_the_write()
    {
        var exception = new EventDeserializationFailureException(12, mapping(), new DivideByZeroException("Boom!"));

        var deadLetter = exception.ToDeadLetterEvent(new JasperFx.Events.Projections.ShardName("Trip", "All", 1));

        // Previously left Guid.Empty for document identity generation to fill in at write time, which
        // meant the creating process could not correlate its ShardFailure with the row it produced.
        deadLetter.Id.ShouldNotBe(Guid.Empty);
        deadLetter.EventSequence.ShouldBe(12);
    }
}
