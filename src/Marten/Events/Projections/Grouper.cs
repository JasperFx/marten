using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections
{
    internal class Grouper<TId, TEvent> : IGrouper<TId>
    {
        private readonly Func<TEvent, TId> _func;

        public Grouper(Func<TEvent, TId> expression)
        {
            // TODO -- it's possible we'll use the expression later to write metadata into the events table
            // to support the async daemon, but I'm doing it the easy way for now
            _func = expression;
        }

        public void Group(IEnumerable<IEvent> events, EventGrouping<TId> grouping)
        {
            var matching = events.Where(x => x.Data is TEvent);
            var groups = matching.GroupBy(x => _func(x.Data.As<TEvent>()));

            foreach (var eventGroups in groups)
            {
                grouping.AddRange(eventGroups.Key, eventGroups);
            }
        }
    }

    public interface IGrouperFactory<TId>
    {
        bool Supports(Type eventType);

        Task<IGrouper<TId>> Create(IQuerySession session, IEvent @event);
    }
}
