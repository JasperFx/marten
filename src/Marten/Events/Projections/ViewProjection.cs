using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Schema.Identity;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Projections
{
    public class ViewProjection<TView, TId>: DocumentProjection<TView>, IDocumentProjection
        where TView : class, new()
    {
        private readonly Func<IQuerySession, TId[], IReadOnlyList<TView>> _sessionLoadMany;
        private readonly IList<IProjectionEventHandler<TView>> eventHandlers = new List<IProjectionEventHandler<TView>>();

        public ViewProjection()
        {
            var loadManyMethod = typeof(IQuerySession).GetMethods()
                .FirstOrDefault(x => x.Name == "LoadMany" && x.GetParameters().Any(y => y.ParameterType == typeof(TId[])));

            if (loadManyMethod == null)
            {
                throw new ArgumentException($"{typeof(TId)} is not supported.");
            }

            var sessionParameter = Expression.Parameter(typeof(IQuerySession), "a");
            var idParameter = Expression.Parameter(typeof(TId[]), "e");
            var body = Expression.Call(sessionParameter, loadManyMethod.MakeGenericMethod(typeof(TView)), idParameter);
            var lambda = Expression.Lambda<Func<IQuerySession, TId[], IReadOnlyList<TView>>>(body, sessionParameter, idParameter);
            _sessionLoadMany = ExpressionCompiler.Compile<Func<IQuerySession, TId[], IReadOnlyList<TView>>>(lambda);
        }

        private class ProjectionHandler
        {
            public IEventIdsSelector<TId> IdsSelector { get; }
            public IProjectionEventHandler<TView> Handler { get; }

            public ProjectionHandler(
                IEventIdsSelector<TId> idsSelector,
                IProjectionEventHandler<TView> handler)
            {
                IdsSelector = idsSelector;
                Handler = handler;
            }
        }

        private class EventProjection
        {
            public TId ViewId { get; }
            public Action<IDocumentSession, TView> Handle { get; }
            public Func<IDocumentSession, TView, Task> HandleAsync { get; }

            private IProjectionEventHandler<TView> handler { get; }

            public EventProjection(ProjectionHandler eventHandler, TId viewId, IEvent @event, object projectionEvent)
            {
                ViewId = viewId;

                HandleAsync = (session, view) => eventHandler.Handler.HandleAsync(session, view, projectionEvent ?? @event.Data);
                Handle = (session, view) => eventHandler.Handler.Handle(session, view, projectionEvent ?? @event.Data);
            }
        }

        private readonly IDictionary<Type, ProjectionHandler> _handlers = new ConcurrentDictionary<Type, ProjectionHandler>();

        public Type[] Consumes => getUniqueEventTypes();
        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public ViewProjection<TView, TId> DeleteEvent<TEvent>() where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionDeleteEventHandler<TView, TEvent>.Create()
            );

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TView, TEvent, bool> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TEvent, TId> viewIdSelector) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create()
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, TId> viewIdSelector,
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, TId> viewIdSelector,
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create()
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector,
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector,
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create()
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create()
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, TId> viewIdSelector,
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, TId> viewIdSelector,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector,
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionDeleteEventHandler<TView, TEvent>.Create(shouldDelete)
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Action<TView, TEvent> handler, bool onlyUpdate = false) where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event);
                    return Task.CompletedTask;
                })
            );

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Action<IDocumentSession, TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event);
                    return Task.CompletedTask;
                })
            );

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Action<TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Action<IDocumentSession, TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, TId> viewIdSelector, Action<TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, TId> viewIdSelector, Action<IDocumentSession, TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Action<TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Action<IDocumentSession, TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Action<TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Action<IDocumentSession, TView, TEvent> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event);
                    return Task.CompletedTask;
                })
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) => handler(view, @event))
            );

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
            => projectEvent<TEvent>(
                IdsSelector.Create((session, @event, streamId) => convertToTId(streamId)),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, handler)
            );

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Func<TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) => handler(view, @event))
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Func<IDocumentSession, TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, handler)
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, TId> viewIdSelector, Func<TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) => handler(view, @event))
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, TId> viewIdSelector, Func<IDocumentSession, TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, handler)
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Func<TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) => handler(view, @event))
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Func<IDocumentSession, TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, handler)
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Func<TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, (IDocumentSession _, TView view, TEvent @event) => handler(view, @event))
            );
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Func<IDocumentSession, TView, TEvent, Task> handler, bool onlyUpdate = false)
            where TEvent : class
        {
            return projectEvent<TEvent>(
                IdsSelector.Create(viewIdsSelector),
                ProjectionCreateOrUpdateEventHandler<TView, TEvent>.Create(onlyUpdate, handler)
            );
        }

        private ViewProjection<TView, TId> projectEvent<TEvent>(
            IEventIdsSelector<TId> idsSelector,
            IProjectionEventHandler<TView> handler) where TEvent : class
        {
            if (idsSelector == null)
                throw new ArgumentException($"{nameof(idsSelector)} or {nameof(idsSelector)} must be provided.");

            //TODO: check why type == ProjectionEventType.CreateAndUpdate should throw argument exception
            //if (handler == null && type == ProjectionEventType.CreateAndUpdate)
            //    throw new ArgumentNullException(nameof(handler));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var eventHandler = new ProjectionHandler(idsSelector, handler);

            _handlers.Add(typeof(TEvent), eventHandler);

            return this;
        }

        private TId convertToTId(Guid streamId)
        {
            if (streamId is TId)
            {
                return (TId)Convert.ChangeType(streamId, typeof(TId));
            }
            else
            {
                throw new InvalidOperationException("IdSelector must be used if Id type is different than Guid.");
            }
        }

        void IProjection.Apply(IDocumentSession session, EventPage page)
        {
            var projections = getEventProjections(session, page);

            var viewIds = projections.Select(projection => projection.ViewId).Distinct().ToArray();

            if (viewIds.Length > 0)
            {
                var views = _sessionLoadMany(session, viewIds);

                applyProjections(session, projections, views);
            }
        }

        async Task IProjection.ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            var projections = getEventProjections(session, page);

            var viewIds = projections.Select(projection => projection.ViewId).Distinct().ToArray();

            if (viewIds.Length > 0)
            {
                var views = _sessionLoadMany(session, viewIds);

                await applyProjectionsAsync(session, projections, views);
            }
        }

        private void applyProjections(IDocumentSession session, ICollection<EventProjection> projections, IEnumerable<TView> views)
        {
            var viewMap = createViewMap(session, projections, views);

            foreach (var eventProjection in projections)
            {
                var hasView = viewMap.TryGetValue(eventProjection.ViewId, out var view);

                if (!hasView)
                    continue;

                eventProjection.Handle(session, view);
            }
        }

        private async Task applyProjectionsAsync(IDocumentSession session, ICollection<EventProjection> projections, IEnumerable<TView> views)
        {
            var viewMap = createViewMap(session, projections, views);

            foreach (var eventProjection in projections)
            {
                var hasView = viewMap.TryGetValue(eventProjection.ViewId, out var view);

                if (!hasView)
                    continue;

                await eventProjection.HandleAsync(session, view);
            }
        }

        private IDictionary<TId, TView> createViewMap(IDocumentSession session, IEnumerable<EventProjection> projections, IEnumerable<TView> views)
        {
            var idAssigner = session.Tenant.IdAssignmentFor<TView>();
            var resolver = session.Tenant.StorageFor<TView>();

            var viewMap = views.ToDictionary(view => (TId)resolver.Identity(view), view => view);

            foreach (var projection in projections)
            {
                var viewId = projection.ViewId;
                var hasExistingView = viewMap.TryGetValue(viewId, out var view);
                if (!hasExistingView)
                {
                    if (projection.Type == ProjectionEventType.CreateAndUpdate)
                    {
                        view = newView(session.Tenant, idAssigner, viewId);
                        viewMap.Add(viewId, view);
                    }
                }

                if (projection.Type == ProjectionEventType.CreateAndUpdate
                    || (projection.Type == ProjectionEventType.UpdateOnly && hasExistingView))
                {
                    session.Store(view);
                }
            }

            return viewMap;
        }

        private static TView newView(ITenant tenant, IdAssignment<TView> idAssigner, TId id)
        {
            var view = new TView();
            idAssigner.Assign(tenant, view, id);
            return view;
        }

        private IList<EventProjection> getEventProjections(IDocumentSession session, EventPage page)
        {
            var projections = new List<EventProjection>();
            foreach (var streamEvent in page.Events)
            {
                var eventType = streamEvent.Data.GetType();
                if (_handlers.TryGetValue(eventType, out var handler))
                {
                    appendProjections(projections, handler, session, streamEvent, eventType, false);
                }
                else
                {
                    var genericEventType = typeof(ProjectionEvent<>).MakeGenericType(eventType);
                    if (_handlers.TryGetValue(genericEventType, out handler))
                    {
                        appendProjections(projections, handler, session, streamEvent, genericEventType, true /* Yeah, genericEventType would always be ProjectionEvent<>. */);
                    }
                }
            }
            return projections;
        }

        private void appendProjections(List<EventProjection> projections, ProjectionHandler handler, IDocumentSession session, IEvent @event, Type eventType, bool isProjectionEvent)
        {
            object projectionEvent = null;
            if (isProjectionEvent)
            {
                var timestamp = @event.Timestamp.UtcDateTime;
                projectionEvent = Activator.CreateInstance(
                    eventType,
                    @event.Id,
                    @event.Version,
                    // Inline projections don't have the timestamp set, set it manually
                    timestamp == default ? DateTime.UtcNow : timestamp,
                    @event.Sequence,
                    @event.StreamId,
                    @event.StreamKey,
                    @event.TenantId,
                    @event.Data
                );
            }

            foreach (var viewId in handler.IdsSelector.Select(session, isProjectionEvent ? projectionEvent : @event.Data, @event.StreamId))
            {
                if (!EqualityComparer<TId>.Default.Equals(viewId, default))
                {
                    projections.Add(new EventProjection(handler, viewId, @event, projectionEvent));
                }
            }
        }

        private Type[] getUniqueEventTypes()
        {
            return _handlers.Keys
                .Distinct()
                .Select(type =>
                {
                    var genericType = type.GenericTypeArguments.FirstOrDefault();
                    if (genericType == null)
                    {
                        return type;
                    }
                    else
                    {
                        return genericType;
                    }
                })
                .ToArray();
        }
    }

    public class ProjectionEvent<T>
    {
        public Guid Id { get; protected set; }
        public int Version { get; protected set; }
        public DateTime Timestamp { get; protected set; }
        public T Data { get; protected set; }
        public long Sequence { get; protected set; }
        public Guid StreamId { get; protected set; }
        public string StreamKey { get; protected set; }
        public string TenantId { get; protected set; }

        public ProjectionEvent(Guid id, int version, DateTime timestamp, long sequence, Guid streamId, string streamKey, string tenantId, T data)
        {
            Id = id;
            Version = version;
            Timestamp = timestamp;
            Sequence = sequence;
            StreamId = streamId;
            StreamKey = streamKey;
            TenantId = tenantId;
            Data = data;
        }
    }
}
