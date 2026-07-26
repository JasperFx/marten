using System;
using System.Runtime.Serialization;
using JasperFx.Events.Daemon;

namespace Marten.Exceptions;

public class UnknownEventTypeException: MartenException, IEventFailureContext
{
    /// <summary>
    ///     The sequence reported when the throw site had no <c>mt_events</c> row in hand — e.g. resolving a
    ///     .NET type name outside the event read path. <see cref="IEventFailureContext.Sequence" /> is
    ///     non-nullable by contract, and -1 is already how Marten's event read path spells "the sequence
    ///     could not be determined".
    /// </summary>
    public const long UnknownSequence = -1;

    public string EventTypeName { get; }

    public UnknownEventTypeException(string eventTypeName): this(eventTypeName, UnknownSequence)
    {
    }

    /// <summary>
    ///     #5048 / jasperfx#565: carry the store-wide sequence of the offending <c>mt_events</c> row when the
    ///     throw site knows it, so a shard paused by an unregistered event type can name the event that
    ///     stopped it instead of only its alias.
    /// </summary>
    public UnknownEventTypeException(string eventTypeName, long sequence): base(
        $"Unknown event type name alias '{eventTypeName}.' You may need to register this event type through StoreOptions.Events.AddEventType(type)")
    {
        EventTypeName = eventTypeName;
        Sequence = sequence;
    }

    protected UnknownEventTypeException(SerializationInfo info, StreamingContext context): base(info, context)
    {
        EventTypeName = string.Empty;
        Sequence = UnknownSequence;
    }

    public long Sequence { get; }

    /// <summary>
    ///     #5048 / jasperfx#565: kept deliberately distinct from
    ///     <see cref="ShardFailureCategory.EventSerialization" />. An alias that resolves to no known .NET
    ///     type in this deployment is normally a missing registration or a rollback past the event type's
    ///     introduction — a deployment fix, not a data fix.
    /// </summary>
    public ShardFailureCategory Category => ShardFailureCategory.UnknownEventType;

    // The type never resolved, so no event was ever materialized to read these from.
    Guid? IEventFailureContext.EventId => null;
    Guid? IEventFailureContext.StreamId => null;
    string? IEventFailureContext.StreamKey => null;
    string? IEventFailureContext.TenantId => null;
    long? IEventFailureContext.Version => null;
}
