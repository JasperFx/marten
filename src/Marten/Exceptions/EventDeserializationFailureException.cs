using System;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon;

namespace Marten.Exceptions;

/// <summary>
///     Thrown if Marten encounters an exception while trying to deserialize
///     or upcast a persisted event
/// </summary>
public class EventDeserializationFailureException: MartenException, IEventFailureContext
{
    public EventDeserializationFailureException(long sequence, IEventType eventType, Exception innerException): base(
        $"Event deserialization error on sequence = {sequence} for event type {eventType.EventTypeName}" , innerException)
    {
        Sequence = sequence;
        EventTypeName = eventType.EventTypeName;
    }

    public long Sequence { get; }

    /// <summary>
    ///     The event store's type alias for the event whose body could not be read (e.g. <c>trip_started</c>).
    ///     #5048 / jasperfx#565: the constructor has always been handed the <see cref="IEventType" /> and used
    ///     it only to build the message string. Retaining the alias lets the daemon report the failing event
    ///     type on <see cref="ShardFailure" /> rather than leaving it buried in prose.
    /// </summary>
    public string? EventTypeName { get; }

    /// <summary>
    ///     #5048 / jasperfx#565: this exception declares its own failure category, so the daemon never has to
    ///     sniff exception type names to classify a paused shard. A body Marten could not deserialize or
    ///     upcast is <see cref="ShardFailureCategory.EventSerialization" /> — a serializer or data problem,
    ///     governed by <c>SkipSerializationErrors</c>.
    /// </summary>
    public ShardFailureCategory Category => ShardFailureCategory.EventSerialization;

    // Everything below is raised while reading an mt_events row, BEFORE there is an IEvent to inspect,
    // so nothing but the sequence and the stored type alias is knowable here. IEventFailureContext makes
    // every one of these nullable for exactly this case.
    Guid? IEventFailureContext.EventId => null;
    Guid? IEventFailureContext.StreamId => null;
    string? IEventFailureContext.StreamKey => null;
    string? IEventFailureContext.TenantId => null;
    long? IEventFailureContext.Version => null;

    internal DeadLetterEvent ToDeadLetterEvent(ShardName name)
    {
        return new DeadLetterEvent
        {
            // #5048 / jasperfx#565: assign the id here rather than leaving it to document identity
            // generation at write time, so the creating process knows the dead letter's id BEFORE the
            // (background, retried) write lands and can correlate it with the ShardFailure it reported.
            // Marten only generates an id when the value is empty, so pre-assigning changes nothing about
            // how the row persists. Version 7 keeps ids time-ordered, matching what jasperfx's
            // DeadLetterEvent constructor now does on the ApplyEventException path.
            Id = Guid.CreateVersion7(),
            EventSequence = Sequence,
            ExceptionMessage = Message,
            ExceptionType = GetType().FullNameInCode(),
            ProjectionName = name.Name,
            ShardName = name.ShardKey
        };
    }
}
