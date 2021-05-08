using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections
{
    internal class MultiStreamGrouper<TId, TEvent> : IGrouper<TId>
    {
        private readonly Func<TEvent, IReadOnlyList<TId>> _func;

        public MultiStreamGrouper(Func<TEvent, IReadOnlyList<TId>> expression)
        {
            // TODO -- it's possible we'll use the expression later to write metadata into the events table
            // to support the async daemon, but I'm doing it the easy way for now
            _func = expression;
        }

        public void Group(IEnumerable<IEvent> events, EventGrouping<TId> grouping)
        {
            var matching = events.Where(x => x.Data is TEvent)
                .SelectMany(@event=> _func(@event.Data.As<TEvent>()).Select(id=> (id, @event)));

            var groups = matching.GroupBy(x => x.id);

            foreach (var eventGroups in groups)
            {
                grouping.AddRange(eventGroups.Key, eventGroups.Select(x => x.@event));
            }
        }
    }

    internal class MultiStreamGrouperFactory<TId, TEvent>: IGrouperFactory<TId>
    {
        private readonly Func<IQuerySession, TEvent, Task<IReadOnlyList<TId>>> _identitiesFunc;

        public MultiStreamGrouperFactory(Func<IQuerySession, TEvent, Task<IReadOnlyList<TId>>> identitiesFunc)
        {
            _identitiesFunc = identitiesFunc;
        }

        public bool Supports(Type eventType) => eventType == typeof(TEvent);

        public async Task<IGrouper<TId>> Create(IQuerySession session, IEvent @event)
        {
            var eventData = (TEvent)@event.Data;
            var ids = await _identitiesFunc( session, eventData);

            return new MultiStreamGrouper<TId, TEvent>(x => Equals(x, eventData) ? ids.ToArray() : Array.Empty<TId>());
        }
    }
}
