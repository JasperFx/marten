#nullable enable
using System;

namespace Marten.Events;

/// <summary>
/// A rule that derives a DCB tag from an event's own data. Registered with
/// <see cref="EventGraph.TagWith{TEvent}"/>.
/// </summary>
public interface IEventTagRule
{
    /// <summary>The event type the rule applies to. Events assignable to it match.</summary>
    Type EventType { get; }

    /// <summary>The tag value for this event, or <c>null</c> when the rule does not apply to it.</summary>
    object? Resolve(object eventData);
}

internal sealed class EventTagRule<TEvent>: IEventTagRule
{
    private readonly Func<TEvent, object?> _rule;

    public EventTagRule(Func<TEvent, object?> rule)
    {
        _rule = rule;
    }

    public Type EventType => typeof(TEvent);

    public object? Resolve(object eventData) => eventData is TEvent typed ? _rule(typed) : null;
}
