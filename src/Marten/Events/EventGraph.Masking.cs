using System;
using System.Collections.Generic;
using JasperFx.Events;

namespace Marten.Events;

public partial class EventGraph
{
    private readonly List<IMasker> _maskers = new();

    /// <summary>
    /// Register a policy for how to remove or mask protected information
    /// for an event type "T" or series of event types that can be cast
    /// to "T"
    /// </summary>
    /// <param name="action"></param>
    /// <typeparam name="T"></typeparam>
    public void AddMaskingRuleForProtectedInformation<T>(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _maskers.Add(new Masker<T>(action));
    }

    internal bool TryMask(IEvent e)
    {
        bool matched = false;
        foreach (var masker in _maskers)
        {
            matched = matched || masker.TryMask(e);
        }

        return matched;
    }
}

internal interface IMasker
{
    bool TryMask(IEvent @event);
}

internal class Masker<T> : IMasker
{
    private readonly Action<T> _masking;

    public Masker(Action<T> masking)
    {
        _masking = masking;
    }

    public bool TryMask(IEvent @event)
    {
        if (@event is IEvent<T> e)
        {
            _masking(e.Data);
            return true;
        }

        return false;
    }
}
