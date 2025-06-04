using System;
using System.Collections.Generic;
using JasperFx.Events;

namespace Marten.Events;

public partial class EventGraph
{
    private readonly List<IMasker> _maskers = [];

    /// <summary>
    /// Register a policy for how to remove or mask protected information
    /// for an event type "T" or series of event types that can be cast
    /// to "T"
    /// </summary>
    /// <param name="action">Action to mask the current object</param>
    /// <typeparam name="T"></typeparam>
    public void AddMaskingRuleForProtectedInformation<T>(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _maskers.Add(new ActionMasker<T>(action));
    }

    /// <summary>
    /// Register a policy for how to remove or mask protected information
    /// for an event type "T" or series of event types that can be cast
    /// to "T"
    /// </summary>
    /// <param name="func">Function to replace the event with a masked event</param>
    /// <typeparam name="T"></typeparam>
    public void AddMaskingRuleForProtectedInformation<T>(Func<T,T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        _maskers.Add(new FuncMasker<T>(func));
    }

    internal bool TryMask(IEvent e)
    {
        var matched = false;
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

internal class ActionMasker<T> : IMasker where T : notnull
{
    private readonly Action<T> _masking;

    public ActionMasker(Action<T> masking)
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

internal class FuncMasker<T> : IMasker where T : notnull
{
    private readonly Func<T,T> _masking;

    public FuncMasker(Func<T,T> masking)
    {
        _masking = masking;
    }

    public bool TryMask(IEvent @event)
    {
        if (@event is IEvent<T> e)
        {
            e.WithData(_masking(e.Data));
            return true;
        }

        return false;
    }
}
