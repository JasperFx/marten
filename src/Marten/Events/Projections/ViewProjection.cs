using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Schema.Identity;
using System.Reflection;
using System.Linq.Expressions;
using Marten.Storage;
using Marten.Util;

namespace Marten.Events.Projections
{
    public class ViewProjection<TView, TId> : DocumentProjection<TView>, IDocumentProjection 
        where TView : class, new()
    {
        private readonly Func<IQuerySession, TId[], IReadOnlyList<TView>> _sessionLoadMany;

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

        private class EventHandler
        {
            public Func<IDocumentSession, object, Guid, TId> IdSelector { get; }
            public Func<IDocumentSession, object, Guid, List<TId>> IdsSelector { get; }
            public Func<IDocumentSession, TView, object, Task> Handler { get; }
            public Func<IDocumentSession, TView, object, Task<bool>> ShouldDelete { get; }
            public ProjectionEventType Type { get; set; }

            public EventHandler(
                Func<IDocumentSession, object, Guid, TId> idSelector,
                Func<IDocumentSession, object, Guid, List<TId>> idsSelector,
                Func<IDocumentSession, TView, object, Task> handler,
                Func<IDocumentSession, TView, object, Task<bool>> shouldDelete,
                ProjectionEventType type)
            {
                IdSelector = idSelector;
                IdsSelector = idsSelector;
                Handler = handler;
                ShouldDelete = shouldDelete ?? defaultShouldDelete;
                Type = type;
            }

            private Task<bool> defaultShouldDelete(IDocumentSession session, TView view, object @event) => Task.FromResult(true);
        }

        private class EventProjection
        {
            public TId ViewId { get; }
            public Func<IDocumentSession, TView, Task> ProjectTo { get; }
            public Func<IDocumentSession, TView, Task<bool>> ShouldDelete { get; }
            public ProjectionEventType Type { get; set; }

            public EventProjection(EventHandler eventHandler, TId viewId, IEvent @event, object projectionEvent)
            {
                ViewId = viewId;
                Type = eventHandler.Type;

                if (Type == ProjectionEventType.Delete)
                {
                    ShouldDelete = (session, view) => eventHandler.ShouldDelete(session, view, projectionEvent ?? @event.Data);
                }
                else
                {
                    ProjectTo = (session, view) => eventHandler.Handler(session, view, projectionEvent ?? @event.Data);
                }
            }
        }

        public enum ProjectionEventType
        {
            Modify,
            Delete
        }

        private readonly IDictionary<Type, EventHandler> _handlers = new ConcurrentDictionary<Type, EventHandler>();

        public Type[] Consumes => getUniqueEventTypes();
        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public ViewProjection<TView, TId> DeleteEvent<TEvent>() where TEvent : class
            => projectEvent<TEvent>(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                null, 
                null, 
                ProjectionEventType.Delete);

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TView, TEvent, bool> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                null, 
                (_, view, @event) => Task.FromResult(shouldDelete(view, @event)), 
                ProjectionEventType.Delete);
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                null, 
                (session, view, @event) => Task.FromResult(shouldDelete(session, view, @event)), 
                ProjectionEventType.Delete);
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TEvent, TId> viewIdSelector) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                null,
                null,
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, TId> viewIdSelector, 
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                null,
                (_, view, @event) => Task.FromResult(shouldDelete(view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, TId> viewIdSelector, 
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                null,
                (session, view, @event) => Task.FromResult(shouldDelete(session, view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null, 
                null,
                null,
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector, 
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null, 
                null,
                (_, view, @event) => Task.FromResult(shouldDelete(view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector, 
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null, 
                null,
                (session, view, @event) => Task.FromResult(shouldDelete(session, view, @event)),
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                null,
                null,
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector, 
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                null,
                (_, view, @event) => Task.FromResult(shouldDelete(view, @event)),
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector, 
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                null,
                (session, view, @event) => Task.FromResult(shouldDelete(session, view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                null,
                null,
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                null,
                (_, view, @event) => Task.FromResult(shouldDelete(view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, bool> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                null,
                (session, view, @event) => Task.FromResult(shouldDelete(session, view, @event)),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                null, 
                (_, view, @event) => shouldDelete(view, @event), 
                ProjectionEventType.Delete);
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
            => projectEvent<TEvent>(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                null, 
                shouldDelete, 
                ProjectionEventType.Delete);
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, TId> viewIdSelector, 
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                null,
                (_, view, @event) => shouldDelete(view, @event),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, TId> viewIdSelector, 
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                null,
                shouldDelete,
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector, 
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null, 
                null,
                (_, view, @event) => shouldDelete(view, @event),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, TId> viewIdSelector, 
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent<TEvent>(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null, 
                null,
                shouldDelete,
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector, 
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                null,
                (_, view, @event) => shouldDelete(view, @event),
                ProjectionEventType.Delete);
        }

        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<TEvent, List<TId>> viewIdsSelector, 
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                null,
                shouldDelete,
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEventAsync<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                null,
                (_, view, @event) => shouldDelete(view, @event),
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> DeleteEvent<TEvent>(
            Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent<TEvent>(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                null,
                shouldDelete,
                ProjectionEventType.Delete);
        }
        
        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Action<TView, TEvent> handler) where TEvent : class
            => projectEvent(
                (session, @event, streamId) => convertToTId(streamId), 
                null,
                (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event); 
                    return Task.CompletedTask;
                });

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Action<IDocumentSession, TView, TEvent> handler) where TEvent : class
            => projectEvent(
                (session, @event, streamId) => convertToTId(streamId), 
                null,
                (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event); 
                    return Task.CompletedTask;
                });

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null,
                (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Action<IDocumentSession, TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null,
                (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, TId> viewIdSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null,
                (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, TId> viewIdSelector, Action<IDocumentSession, TView, TEvent> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null,
                (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent),
                (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Action<IDocumentSession, TView, TEvent> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent),
                (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Action<TView, TEvent> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent),
                (IDocumentSession _, TView view, TEvent @event) =>
                {
                    handler(view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEvent<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Action<IDocumentSession, TView, TEvent> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent),
                (IDocumentSession session, TView view, TEvent @event) =>
                {
                    handler(session, view, @event); 
                    return Task.CompletedTask;
                });
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TView, TEvent, Task> handler) where TEvent : class
            => projectEvent(
                (session, @event, streamId) => convertToTId(streamId), 
                null, 
                (IDocumentSession _, TView view, TEvent @event) => handler(view, @event));

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TView, TEvent, Task> handler) where TEvent : class
            => projectEvent((session, @event, streamId) => convertToTId(streamId), null, handler);
        
        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Func<TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(session, @event as TEvent), 
                null,  
                (IDocumentSession _, TView view, TEvent @event) => handler(view, @event));
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, TId> viewIdSelector, Func<IDocumentSession, TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent((session, @event, streamId) => viewIdSelector(session, @event as TEvent), null, handler);
        }
        
        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, TId> viewIdSelector, Func<TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent(
                (session, @event, streamId) => viewIdSelector(@event as TEvent), 
                null, 
                (IDocumentSession _, TView view, TEvent @event) => handler(view, @event));
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, TId> viewIdSelector, Func<IDocumentSession, TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdSelector == null) throw new ArgumentNullException(nameof(viewIdSelector));
            return projectEvent((session, @event, streamId) => viewIdSelector(@event as TEvent), null, handler);
        }
        
        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Func<TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), 
                (IDocumentSession _, TView view, TEvent @event) => handler(view, @event));
        }
        
        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<IDocumentSession, TEvent, List<TId>> viewIdsSelector, Func<IDocumentSession, TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(null, (session, @event, streamId) => viewIdsSelector(session, @event as TEvent), handler);
        }
        
        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Func<TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(
                null, 
                (session, @event, streamId) => viewIdsSelector(@event as TEvent), 
                (IDocumentSession _, TView view, TEvent @event) => handler(view, @event));
        }

        public ViewProjection<TView, TId> ProjectEventAsync<TEvent>(Func<TEvent, List<TId>> viewIdsSelector, Func<IDocumentSession, TView, TEvent, Task> handler) where TEvent : class
        {
            if (viewIdsSelector == null) throw new ArgumentNullException(nameof(viewIdsSelector));
            return projectEvent(null, (session, @event, streamId) => viewIdsSelector(@event as TEvent), handler);
        }
        
        private ViewProjection<TView, TId> projectEvent<TEvent>(
            Func<IDocumentSession, object, Guid, TId> viewIdSelector,
            Func<IDocumentSession, object, Guid, List<TId>> viewIdsSelector,
            Func<IDocumentSession, TView, TEvent, Task> handler,
            Func<IDocumentSession, TView, TEvent, Task<bool>> shouldDelete = null,
            ProjectionEventType type = ProjectionEventType.Modify) where TEvent : class
        {
            if (viewIdSelector == null && viewIdsSelector == null) 
                throw new ArgumentException($"{nameof(viewIdSelector)} or {nameof(viewIdsSelector)} must be provided.");
            if (handler == null && type == ProjectionEventType.Modify) 
                throw new ArgumentNullException(nameof(handler));

            EventHandler eventHandler;
            if (type == ProjectionEventType.Modify)
            {
                eventHandler = new EventHandler(
                    viewIdSelector, 
                    viewIdsSelector,
                    (session, view, @event) => handler(session, view, @event as TEvent),
                    null,
                    type);
            }
            else
            {
                eventHandler = new EventHandler(
                    viewIdSelector, 
                    viewIdsSelector, 
                    null,
                    shouldDelete == null 
                        ? (Func<IDocumentSession, TView, object, Task<bool>>)null 
                        : (session, view, @event) => shouldDelete(session, view, @event as TEvent),
                    type);
            }

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
                var view = viewMap[eventProjection.ViewId];

                using(Util.NoSynchronizationContextScope.Enter())
                {
                    if (eventProjection.Type == ProjectionEventType.Delete)
                    {
                        var shouldDeleteTask = eventProjection.ShouldDelete(session, view);
                        shouldDeleteTask.Wait();
                        if (shouldDeleteTask.Result)
                        {
                            session.Delete(view);
                        }
                    }
                    else
                    {
                        eventProjection.ProjectTo(session, view).Wait();
                    }
                }
            }
        }

        private async Task applyProjectionsAsync(IDocumentSession session, ICollection<EventProjection> projections, IEnumerable<TView> views)
        {
            var viewMap = createViewMap(session, projections, views);

            foreach (var eventProjection in projections)
            {
                var view = viewMap[eventProjection.ViewId];

                if (eventProjection.Type == ProjectionEventType.Delete)
                {
                    if (await eventProjection.ShouldDelete(session, view))
                    {
                        session.Delete(view);
                    }
                }
                else
                {
                    await eventProjection.ProjectTo(session, view);
                }
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
                TView view;
                if (!viewMap.TryGetValue(viewId, out view))
                {
                    view = newView(session.Tenant, idAssigner, viewId);
                    viewMap.Add(viewId, view);
                }

                if (projection.Type == ProjectionEventType.Modify)
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
                EventHandler handler;
                var eventType = streamEvent.Data.GetType();
                if (_handlers.TryGetValue(eventType, out handler))
                {
                    appendProjections(projections, handler, session, streamEvent, eventType, false);
                }
                else
                {
                    var genericEventType = typeof(ProjectionEvent<>).MakeGenericType(eventType);
                    if (_handlers.TryGetValue(genericEventType, out handler))
                    {
                        appendProjections(projections, handler, session, streamEvent, genericEventType, true);
                    }
                }
            }
            return projections;
        }

        private void appendProjections(List<EventProjection> projections, EventHandler handler, IDocumentSession session, IEvent streamEvent, Type eventType, bool isProjectionEvent)
        {
            object projectionEvent = null;
            if (isProjectionEvent)
            {
                var timestamp = streamEvent.Timestamp.UtcDateTime;
                projectionEvent = Activator.CreateInstance(
                    eventType,
                    streamEvent.Id,
                    streamEvent.Version,
                    // Inline projections don't have the timestamp set, set it manually
                    timestamp == default(DateTime) ? DateTime.UtcNow : timestamp,
                    streamEvent.Data);
            }

            if (handler.IdSelector != null)
            {
                var viewId = handler.IdSelector(session, isProjectionEvent ? projectionEvent : streamEvent.Data, streamEvent.StreamId);
                projections.Add(new EventProjection(handler, viewId, streamEvent, projectionEvent));
            }
            else
            {
                foreach (var viewId in handler.IdsSelector(session, isProjectionEvent ? projectionEvent : streamEvent.Data, streamEvent.StreamId))
                {
                    projections.Add(new EventProjection(handler, viewId, streamEvent, projectionEvent));
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

        public ProjectionEvent(Guid id, int version, DateTime timestamp, T data)
        {
            Id = id;
            Version = version;
            Timestamp = timestamp;
            Data = data;
        }
    }
}