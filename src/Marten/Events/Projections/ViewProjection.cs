using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public class ViewProjection<TView> : DocumentProjection<TView>, IDocumentProjection where TView : class, new()
    {
        private class EventHandler
        {
            public Func<IDocumentSession, object, Guid, Guid> IdSelector { get; }
            public Action<TView, object> Handler { get; }

            public EventHandler(Func<IDocumentSession, object, Guid, Guid> idSelector, Action<TView, object> handler)
            {
                IdSelector = idSelector;
                Handler = handler;
            }
        }

        private class EventProjection
        {
            public Guid ViewId { get; }
            public Action<TView> ProjectTo { get; }

            public EventProjection(EventHandler eventHandler, Guid viewId, IEvent @event)
            {
                ViewId = viewId;
                ProjectTo = view => eventHandler.Handler(view, @event.Data);
            }
        }

        private readonly IDictionary<Type, EventHandler> _handlers = new ConcurrentDictionary<Type, EventHandler>();

        public Type[] Consumes => getUniqueEventTypes();
        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public ViewProjection<TView> ProjectEvent<TEvent>(Action<TView, TEvent> handler) where TEvent : class
            => projectEvent((session, @event, streamId) => streamId, handler);

        public ViewProjection<TView> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, Guid> viewIdSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent((session, @event, streamId) => viewIdSelector(session, @event as TEvent), handler);
        }

        public ViewProjection<TView> ProjectEvent<TEvent>(Func<TEvent, Guid> viewIdSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent((session, @event, streamId) => viewIdSelector(@event as TEvent), handler);
        }

        private ViewProjection<TView> projectEvent<TEvent>(Func<IDocumentSession, object, Guid, Guid> viewIdSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventHandler = new EventHandler(viewIdSelector, (view, @event) => handler(view, @event as TEvent));

            _handlers.Add(typeof(TEvent), eventHandler);

            return this;
        }

        void IProjection.Apply(IDocumentSession session, EventStream[] streams)
        {
            var projections = getEventProjections(session, streams);

            var viewIds = projections.Select(projection => projection.ViewId).Distinct().ToArray();
            var views = session.LoadMany<TView>(viewIds);

            applyProjections(session, projections, views);
        }

        async Task IProjection.ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            var projections = getEventProjections(session, streams);

            var viewIds = projections.Select(projection => projection.ViewId).Distinct().ToArray();
            var views = await session.LoadManyAsync<TView>(token, viewIds).ConfigureAwait(false);

            applyProjections(session, projections, views);
        }

        private void applyProjections(IDocumentSession session, IList<EventProjection> projections, IList<TView> views)
        {
            var viewMap = createViewMap(session, projections, views);

            foreach (var eventProjection in projections)
            {
                var view = viewMap[eventProjection.ViewId];

                eventProjection.ProjectTo(view);
            }
        }

        private IDictionary<Guid, TView> createViewMap(IDocumentSession session, IList<EventProjection> projections, IList<TView> views)
        {
            var idAssigner = session.Tenant.IdAssignmentFor<TView>();
            var resolver = session.Tenant.StorageFor<TView>();

            var viewMap =  views.ToDictionary(view => (Guid) resolver.Identity(view), view => view);

            foreach (var projection in projections)
            {
                var viewId = projection.ViewId;
                TView view;
                if (!viewMap.TryGetValue(viewId, out view))
                {
                    view = newView(session.Tenant, idAssigner, viewId);
                    viewMap.Add(viewId, view);
                }
                session.Store(view);
            }

            return viewMap;
        }

        private static TView newView(ITenant tenant, IdAssignment<TView> idAssigner, Guid id)
        {
            var view = new TView();
            idAssigner.Assign(tenant, view, id);
            return view;
        }

        private IList<EventProjection> getEventProjections(IDocumentSession session, EventStream[] streams)
        {
            var streamEvents = streams.SelectMany(stream => stream.Events.Select(@event => new { StreamId = stream.Id, Event = @event } ));

            var projections = new List<EventProjection>();
            foreach (var streamEvent in streamEvents)
            {
                EventHandler handler;
                if (_handlers.TryGetValue(streamEvent.Event.Data.GetType(), out handler))
                {
                    var viewId = handler.IdSelector(session, streamEvent.Event.Data, streamEvent.StreamId);
                    projections.Add(new EventProjection(handler, viewId, streamEvent.Event));
                }
            }
            return projections;
        }

        private Type[] getUniqueEventTypes()
        {
            return _handlers.Keys
                .Union(_handlers.Keys)
                .Distinct()
                .ToArray();
        }
    }
}