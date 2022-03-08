using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Assigns an event to only one stream
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <typeparam name="TEvent"></typeparam>
    internal class SingleStreamGrouper<TId, TEvent> : IGrouper<TId>
    {
        private readonly Func<TEvent, TId> _func;

        public SingleStreamGrouper(Func<TEvent, TId> expression)
        {
            _func = expression;
        }

        public void Apply(IEnumerable<IEvent> events, ITenantSliceGroup<TId> grouping)
        {
            grouping.AddEvents(_func, events);
        }
    }
}
