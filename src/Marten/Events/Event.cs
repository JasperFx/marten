using System;
using Baseline;
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

        /// <summary>
        /// If using Guid's for the stream identity, this will
        /// refer to the Stream's Id, otherwise it will always be Guid.Empty
        /// </summary>
        Guid StreamId { get; set; }

        /// <summary>
        /// If using strings as the stream identifier, this will refer
        /// to the containing Stream's Id
        /// </summary>
        string StreamKey { get; set; }

        /// <summary>
        /// The UTC time that this event was originally captured
        /// </summary>
        DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// If using multi-tenancy by tenant id
        /// </summary>
        string TenantId { get; set; }

        void Apply<TAggregate>(TAggregate state, IAggregator<TAggregate> aggregator)
            where TAggregate : class, new();
    }
    // ENDSAMPLE

    public class Event<T> : IEvent
    {
        public Event(T data)
        {
            Data = data;
        }

        // SAMPLE: event_metadata
        /// <summary>
        /// A reference to the stream that contains
        /// this event
        /// </summary>
        public Guid StreamId { get; set; }

        /// <summary>
        /// A reference to the stream if the stream
        /// identier mode is AsString
        /// </summary>
        public string StreamKey { get; set; }

        /// <summary>
        /// An alternative Guid identifier to identify
        /// events across databases
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// An event's version position within its event stream
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// A global sequential number identifying the Event
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// The actual event data
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// The UTC time that this event was originally captured
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        public string TenantId { get; set; }
        // ENDSAMPLE


        object IEvent.Data => Data;



        public virtual void Apply<TAggregate>(TAggregate state, IAggregator<TAggregate> aggregator)
            where TAggregate : class, new()
        {
            var step = aggregator.AggregatorFor<T>();
            if (step is IAggregationWithMetadata<TAggregate, T>)
            {
                step.As<IAggregationWithMetadata<TAggregate, T>>()
                    .Apply(state, this);
            }
            else
            {
                step?.Apply(state, Data);
            }

            
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
