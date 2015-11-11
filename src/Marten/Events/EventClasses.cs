using System;
using System.Collections;
using System.Collections.Generic;
using FubuCore;
using FubuCore.Util;
using Marten.Schema;

namespace Marten.Events
{
    public interface IEvent
    {
        Guid Id { get; set; }
    }

    public interface IAggregate
    {
        Guid Id { get; set; }
    }

    public interface IEvents
    {
        void Append<T>(Guid stream, T @event) where T : IEvent;

        void AppendEvents(Guid stream, params IEvent[] events);

        Guid StartStream<T>(params IEvent[] events);

        T FetchSnapshot<T>(Guid streamId) where T : IAggregate;

        IEnumerable<IEvent> FetchStream<T>(Guid stringId) where T : IAggregate;

        void DeleteEvent<T>(Guid id);
        void DeleteEvent<T>(T @event) where T : IEvent;


        void ReplaceEvent<T>(T @event);
    }

    public enum ProjectionTiming
    {
        inline,
        live,
        async
    }



    public class StreamMapping : DocumentMapping
    {
        public StreamMapping(Type aggregateType) : base(aggregateType)
        {
            if (!aggregateType.CanBeCastTo<IAggregate>()) throw new ArgumentOutOfRangeException(nameof(aggregateType), $"Only types implementing {typeof(IAggregate)} can be accepted");
        }
    }

    public class EventMapping : DocumentMapping 
    {
        public EventMapping(StreamMapping parent, Type eventType) : base(eventType)
        {
            if (!eventType.CanBeCastTo<IEvent>()) throw new ArgumentOutOfRangeException(nameof(eventType), $"Only types implementing {typeof(IEvent)} can be accepted");
        }
    }
}