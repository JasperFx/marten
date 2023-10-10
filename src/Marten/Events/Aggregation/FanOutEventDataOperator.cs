using System;
using System.Collections.Generic;
using Marten.Events.Projections;

namespace Marten.Events.Aggregation;

internal abstract class FanOutOperator<TSource>: IFanOutRule
{
    public FanoutMode Mode { get; set; } = FanoutMode.AfterGrouping;

    public Type OriginatingType => typeof(TSource);

    public abstract void Apply(List<IEvent> events);
}

internal class FanOutEventDataOperator<TSource, TTarget>: FanOutOperator<TSource>
{
    private readonly Func<TSource, IEnumerable<TTarget>> _fanOutFunc;

    public FanOutEventDataOperator(Func<TSource, IEnumerable<TTarget>> fanOutFunc)
    {
        _fanOutFunc = fanOutFunc;
    }

    public FanoutMode Mode { get; set; } = FanoutMode.AfterGrouping;

    public Type OriginatingType => typeof(TSource);

    public override void Apply(List<IEvent> events)
    {
        events.FanOutEventData(_fanOutFunc);
    }
}

internal class FanOutEventOperator<TSource, TTarget>: FanOutOperator<TSource>
{
    private readonly Func<IEvent<TSource>, IEnumerable<TTarget>> _fanOutFunc;

    public FanOutEventOperator(Func<IEvent<TSource>, IEnumerable<TTarget>> fanOutFunc)
    {
        _fanOutFunc = fanOutFunc;
    }

    public override void Apply(List<IEvent> events)
    {
        events.FanOut(_fanOutFunc);
    }
}
