#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events;

/// <summary>
/// A rule that derives DCB tags from an event's own data. Registered with
/// <see cref="EventGraph.TagWith{TEvent}"/> or <see cref="EventGraph.TagEventsBy"/>.
/// </summary>
public interface IEventTagRule
{
    /// <summary>What the rule applies to, for error messages.</summary>
    string Description { get; }

    /// <summary>The tags for this event. Empty when the rule does not apply to it.</summary>
    IEnumerable<object?> Resolve(object eventData);
}

internal sealed class EventTagRule<TEvent>: IEventTagRule
{
    private readonly Func<TEvent, object?> _rule;

    public EventTagRule(Func<TEvent, object?> rule)
    {
        _rule = rule;
    }

    public string Description => typeof(TEvent).FullName!;

    public IEnumerable<object?> Resolve(object eventData)
    {
        if (eventData is not TEvent typed) yield break;

        var tag = _rule(typed);
        if (tag != null) yield return tag;
    }
}

internal sealed class StoreWideEventTagRule: IEventTagRule
{
    private readonly Func<object, IEnumerable<object>?> _tagger;

    public StoreWideEventTagRule(Func<object, IEnumerable<object>?> tagger)
    {
        _tagger = tagger;
    }

    public string Description => "the store-wide rule registered with TagEventsBy()";

    public IEnumerable<object?> Resolve(object eventData) => _tagger(eventData) ?? Enumerable.Empty<object>();
}
