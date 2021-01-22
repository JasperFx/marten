using System;
using System.Collections.Generic;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public interface IEventSlice
    {
        IReadOnlyList<IEvent> Events { get; }
    }

    public class EventSlice<TDoc, TId>: IEventSlice
    {
        private readonly List<IEvent> _events = new List<IEvent>();

        public EventSlice(TId id, ITenant tenant, IEnumerable<IEvent> events = null)
        {
            Id = id;
            Tenant = tenant;
            if (events != null)
            {
                _events.AddRange(events);
            }
        }

        /// <summary>
        /// Is this action the start of a new stream or appending
        /// to an existing stream?
        /// </summary>
        public StreamActionType ActionType => _events[0].Version == 1 ? StreamActionType.Start : StreamActionType.Append;


        public TId Id { get; }
        public ITenant Tenant { get; }

        public TDoc Aggregate { get; set; }

        public void AddEvent(IEvent e)
        {
            _events.Add(e);
        }

        public void AddEvents(IEnumerable<IEvent> events)
        {
            _events.AddRange(events);
        }

        public IReadOnlyList<IEvent> Events => _events;

        internal void ApplyFanOutRules(IEnumerable<IFanOutRule> rules)
        {
            foreach (var rule in rules)
            {
                rule.Apply(_events);
            }
        }
    }
}
