using System.Collections.Generic;
using System.Linq;
using Marten.Storage;
#nullable enable
namespace Marten.Events.Projections
{
    public interface IEventSlice
    {
        IReadOnlyList<IEvent> Events();
    }

    /// <summary>
    /// A grouping of events that will be applied to an aggregate of type TDoc
    /// with the identity TId
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TId"></typeparam>
    public class EventSlice<TDoc, TId>: IEventSlice
    {
        private readonly List<IEvent> _events = new List<IEvent>();

        public EventSlice(TId id, ITenant tenant, IEnumerable<IEvent>? events = null)
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


        /// <summary>
        /// The aggregate identity
        /// </summary>
        public TId Id { get; }

        /// <summary>
        /// The current tenant
        /// </summary>
        public ITenant Tenant { get; }

        /// <summary>
        /// The related aggregate document
        /// </summary>
        public TDoc? Aggregate { get; set; }

        /// <summary>
        /// Add a single event to this slice
        /// </summary>
        /// <param name="e"></param>
        public void AddEvent(IEvent e)
        {
            _events.Add(e);
        }

        /// <summary>
        /// Add a grouping of events to this slice
        /// </summary>
        /// <param name="events"></param>
        public void AddEvents(IEnumerable<IEvent> events)
        {
            _events.AddRange(events);
        }

        public int Count => _events.Count;

        /// <summary>
        /// All the events in this slice
        /// </summary>
        public IReadOnlyList<IEvent> Events() => _events;

        internal void ApplyFanOutRules(IEnumerable<IFanOutRule> rules)
        {
            // Need to do this first before applying the fanout rules
            var events = _events.Distinct().OrderBy(x => x.Version).ToArray();
            _events.Clear();
            _events.AddRange(events);

            foreach (var rule in rules)
            {
                rule.Apply(_events);
            }
        }
    }
}
