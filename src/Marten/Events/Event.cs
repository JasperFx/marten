using System;
using Marten.Events.Projections;

namespace Marten.Events
{
    public class Event
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public object Data { get; set; }

        public virtual void Apply<TAggregate>(TAggregate state, Aggregator<TAggregate> aggregator)
            where TAggregate : class, new()
        {
            // Nothing
        }
    }
}
