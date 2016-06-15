using System;
using Marten.Events.Projections;

namespace Marten.Events
{
    // SAMPLE: IEvent
    public interface IEvent
    {
        Guid Id { get; set; }
        int Version { get; set; }

        long Sequence { get; set; }

        /// <summary>
        /// The actual event data body
        /// </summary>
        object Data { get; }

        Guid StreamId { get; set; }

        void Apply<TAggregate>(TAggregate state, IAggregator<TAggregate> aggregator)
            where TAggregate : class, new();
    }

    public class Event<T> : IEvent
    {
        public Event(T data)
        {
            Data = data;
        }

        public Guid StreamId { get; set; }

        public Guid Id { get; set; }
        public int Version { get; set; }
        public long Sequence { get; set; }
        public T Data { get; set; }

        object IEvent.Data => Data;

        public virtual void Apply<TAggregate>(TAggregate state, IAggregator<TAggregate> aggregator)
            where TAggregate : class, new()
        {
            aggregator.AggregatorFor<T>()?.Apply(state, Data);
        }

        protected bool Equals(Event<T> other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Event<T>) obj);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
    // ENDSAMPLE
}
