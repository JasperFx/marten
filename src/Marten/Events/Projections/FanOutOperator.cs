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

        public void Apply(List<IEvent> events)
        {
            var matches = events.OfType<Event<TSource>>().ToArray();
            var starting = 0;
            foreach (var source in matches)
            {
                var index = events.IndexOf(source, starting);
                var range = _fanOutFunc(source.Data).Select(x => new Event<TTarget>(x)
                {
                    Id = source.Id,
                    Sequence = source.Sequence,
                    TenantId = source.TenantId,
                    Version = source.Version,
                    StreamId = source.StreamId,
                    StreamKey = source.StreamKey,
                    Timestamp = source.Timestamp
                }).ToArray();

                events.InsertRange(index + 1, range);

                starting = index + range.Length;
            }
        }
    }
}
