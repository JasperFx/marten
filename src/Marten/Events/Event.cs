using System;
using Marten.Events.Projections;

namespace Marten.Events
{
    public interface IEvent
    {
        Guid Id { get; set; }
        int Version { get; set; }
        object Data { get; }

        void Apply<TAggregate>(TAggregate state, Aggregator<TAggregate> aggregator)
            where TAggregate : class, new();
    }

    public class Event<T> : IEvent
    {
        public Event(T data)
        {
            Data = data;
        }

        public Guid Id { get; set; }
        public int Version { get; set; }
        public T Data { get; set; }

        object IEvent.Data => Data;

        public virtual void Apply<TAggregate>(TAggregate state, Aggregator<TAggregate> aggregator)
            where TAggregate : class, new()
        {
            aggregator.AggregatorFor<T>()?.Apply(state, Data);
        }
    }
}
