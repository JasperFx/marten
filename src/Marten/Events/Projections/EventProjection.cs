using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;

namespace Marten.Events.Projections
{
    public class EventProjection<TView> : IProjection
    {
        private class StreamEvent
        {
            public EventStream Stream { get; }
            public IEvent Event { get; }

            public StreamEvent(EventStream stream, IEvent @event)
            {
                Stream = stream;
                Event = @event;
            }
        }

        private readonly IDictionary<Type, Action<IDocumentSession, StreamEvent>> _handlers = new ConcurrentDictionary<Type, Action<IDocumentSession, StreamEvent>>();
        private readonly IDictionary<Type, Func<IDocumentSession, StreamEvent, Task>> _asyncHandlers = new ConcurrentDictionary<Type, Func<IDocumentSession, StreamEvent, Task>>();

        public Type[] Consumes => _handlers.Keys.Union(_asyncHandlers.Keys).ToArray();
        public Type Produces => typeof(TView);
        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public EventProjection<TView> Event<TEvent>(Action<IDocumentSession, Guid, TEvent> handler) where TEvent : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlers.Add(typeof(TEvent), (session, streamEvent) => handler(session, streamEvent.Stream.Id, streamEvent.Event.Data as TEvent));

            return this;
        }

        public EventProjection<TView> EventAsync<TEvent>(Func<IDocumentSession, Guid, TEvent, Task> handler) where TEvent : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _asyncHandlers.Add(typeof(TEvent), (session, streamEvent) => handler(session, streamEvent.Stream.Id, streamEvent.Event.Data as TEvent));

            return this;
        }

        public void Apply(IDocumentSession session, EventStream[] streams)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            var events = GetEvents(streams);

            foreach (var @event in events)
            {
                Action<IDocumentSession, StreamEvent> handler;
                if (_handlers.TryGetValue(@event.Event.Data.GetType(), out handler))
                {
                    handler(session, @event);
                }
            }
        }

        public async Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            var events = GetEvents(streams);

            foreach (var @event in events)
            {
                Func<IDocumentSession, StreamEvent, Task> handler;
                if (_asyncHandlers.TryGetValue(@event.Event.Data.GetType(), out handler))
                {
                    await handler(session, @event);
                }
            }
        }

        private static IEnumerable<StreamEvent> GetEvents(EventStream[] streams)
        {
            return streams.SelectMany(stream => stream.Events.Select(@event => new StreamEvent(stream, @event)));
        }
    }
}