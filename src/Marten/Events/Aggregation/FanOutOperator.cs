using System;
using System.Collections.Generic;
using Marten.Events.Projections;

namespace Marten.Events.Aggregation
{
    internal class FanOutOperator<TSource, TTarget> : IFanOutRule
    {
        private readonly Func<TSource, IEnumerable<TTarget>> _fanOutFunc;

        public FanOutOperator(Func<TSource, IEnumerable<TTarget>> fanOutFunc)
        {
            _fanOutFunc = fanOutFunc;
        }

        public FanoutMode Mode { get; set; } = FanoutMode.AfterGrouping;

        public Type OriginatingType => typeof(TSource);

        public void Apply(List<IEvent> events)
        {
            events.FanOut(_fanOutFunc);
        }
    }
}
