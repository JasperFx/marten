using System;
using JasperFx.CodeGeneration;
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
public class EventDeserializationFailureException: MartenException
{
    public EventDeserializationFailureException(long sequence, IEventType eventType, Exception innerException): base(
        $"Event deserialization error on sequence = {sequence} for event type {eventType.EventTypeName}" , innerException)
    {
        Sequence = sequence;
    }

    public long Sequence { get; }

    internal DeadLetterEvent ToDeadLetterEvent(ShardName name)
    {
        return new DeadLetterEvent
        {
            EventSequence = Sequence,
            ExceptionMessage = Message,
            ExceptionType = GetType().FullNameInCode(),
            ProjectionName = name.ProjectionName,
            ShardName = name.Key
        };
    }
}
