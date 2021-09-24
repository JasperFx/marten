using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.Projections
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
            var matches = events.OfType<Event<TSource>>().ToArray();
            var starting = 0;
            foreach (var source in matches)
            {
                var index = events.IndexOf(source, starting);
                var range = _fanOutFunc(source.Data).Select(x => source.WithData(x)).ToArray();

                events.InsertRange(index + 1, range);

                starting = index + range.Length;
            }
        }
    }
}
