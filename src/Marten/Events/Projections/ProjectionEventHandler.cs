using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.Events.Projections
{
    internal interface IProjectionEventHandler<TAggregate>
    {
        Type[] Handles { get; }

        void Handle(IDocumentSession session, TAggregate state, object @event);

        Task HandleAsync(IDocumentSession session, TAggregate state, object @event);

        bool CanHandle(Type eventType);

        bool CanHandle(EventStream stream);
    }

    internal abstract class ProjectionEventHandler<TAggregate>: IProjectionEventHandler<TAggregate> where TAggregate : class, new()
    {
        public abstract Type[] Handles { get; }

        public virtual bool CanHandle(Type eventType)
        {
            return Handles.Contains(eventType);
        }

        public bool CanHandle(EventStream stream)
        {
            return stream.Events.Any(x => Handles.Contains(x.Data.GetType()));
        }

        public abstract Task HandleAsync(IDocumentSession session, TAggregate state, object @event);

        public abstract void Handle(IDocumentSession session, TAggregate state, object @event);
    }

    internal class ProjectionDeleteEventHandler<TAggregate, TEvent>: ProjectionEventHandler<TAggregate> where TAggregate : class, new()
    {
        public override Type[] Handles => handles;

        private readonly Type[] handles;

        private readonly Func<IDocumentSession, TAggregate, TEvent, Task<bool>> shouldDelete;

        private static readonly Func<IDocumentSession, TAggregate, TEvent, Task<bool>> defaultShouldDelete =
            (IDocumentSession session, TAggregate view, TEvent @event) => Task.FromResult(true);

        public ProjectionDeleteEventHandler(
            Func<IDocumentSession, TAggregate, TEvent, Task<bool>> shouldDelete
        )
        {
            handles = new[] { typeof(TEvent) };
            this.shouldDelete = shouldDelete ?? defaultShouldDelete;
        }

        public override void Handle(IDocumentSession session, TAggregate state, object @event)
        {
            //TODO: Maybe a bit more of defensive programmming instead straight typecasting?
            using (NoSynchronizationContextScope.Enter())
            {
                if (shouldDelete(session, state, (TEvent)@event).Result)
                    session.Delete(state);
            }
        }

        public override async Task HandleAsync(IDocumentSession session, TAggregate state, object @event)
        {
            //TODO: Fix async here and in constructor
            //TODO: Maybe a bit more of defensive programmming instead straight typecasting?
            if (await shouldDelete(session, state, (TEvent)@event))
                session.Delete(state);
        }

        public static ProjectionDeleteEventHandler<TAggregate, TEvent> Create()
        {
            return new ProjectionDeleteEventHandler<TAggregate, TEvent>(null);
        }

        public static ProjectionDeleteEventHandler<TAggregate, TEvent> Create(
            Func<IDocumentSession, TAggregate, TEvent, Task<bool>> shouldDelete
        )
        {
            return new ProjectionDeleteEventHandler<TAggregate, TEvent>(shouldDelete);
        }

        public static ProjectionDeleteEventHandler<TAggregate, TEvent> Create(
            Func<TAggregate, TEvent, Task<bool>> shouldDelete
        )
        {
            return Create(
                shouldDelete != null ?
                    (IDocumentSession _, TAggregate aggregate, TEvent @event) => shouldDelete(aggregate, @event)
                    : defaultShouldDelete
            );
        }

        public static ProjectionDeleteEventHandler<TAggregate, TEvent> Create(
            Func<IDocumentSession, TAggregate, TEvent, bool> shouldDelete
        )
        {
            return Create(
                shouldDelete != null ?
                    (IDocumentSession session, TAggregate aggregate, TEvent @event) => Task.FromResult(shouldDelete(session, aggregate, @event))
                    : defaultShouldDelete
            );
        }

        public static ProjectionDeleteEventHandler<TAggregate, TEvent> Create(
            Func<TAggregate, TEvent, bool> shouldDelete
        )
        {
            return Create(
                shouldDelete != null ?
                    (IDocumentSession _, TAggregate aggregate, TEvent @event) => Task.FromResult(shouldDelete(aggregate, @event))
                    : defaultShouldDelete
            );
        }
    }

    internal class ProjectionAggregateEventHandler<TAggregate>: ProjectionEventHandler<TAggregate> where TAggregate : class, new()
    {
        public override Type[] Handles => aggregator.EventTypes;

        private readonly IAggregator<TAggregate> aggregator;

        public ProjectionAggregateEventHandler(IAggregator<TAggregate> aggregator)
        {
            this.aggregator = aggregator;
        }

        public override void Handle(IDocumentSession session, TAggregate state, object @event)
        {
            //TODO: Make sure that throwing exception in this place is valid
            Handle(session, state, @event as IEvent ?? throw new ArgumentException("ProjectionAggregateEventHandler supports only IEvent"));
        }

        private void Handle(IDocumentSession session, TAggregate state, IEvent @event)
        {
            @event.Apply(state, aggregator);
            session.Store(state);
        }

        public override Task HandleAsync(IDocumentSession session, TAggregate state, object @event)
        {
            Handle(session, state, @event);

            return Task.CompletedTask;
        }
    }

    internal class ProjectionCreateOrUpdateEventHandler<TAggregate, TEvent>: ProjectionEventHandler<TAggregate> where TAggregate : class, new()
    {
        public override Type[] Handles => handles;

        //TODO: this probably shouldn't be here, also it's not handled yet
        private readonly bool onlyUpdate;

        private readonly Func<TAggregate> aggregateFactory;

        private readonly Type[] handles;

        private readonly Func<IDocumentSession, TAggregate, TEvent, Task> Apply;

        public ProjectionCreateOrUpdateEventHandler(
            bool onlyUpdate,
            Func<TAggregate> aggregateFactory,
            Func<IDocumentSession, TAggregate, TEvent, Task> apply
        )
        {
            this.onlyUpdate = onlyUpdate;
            this.aggregateFactory = aggregateFactory;
            handles = new[] { typeof(TEvent) };

            Apply = apply;
        }

        public override void Handle(IDocumentSession session, TAggregate state, object @event)
        {
            using (NoSynchronizationContextScope.Enter())
            {
                //TODO: Maybe a bit more of defensive programmming instead straight typecasting?
                Apply(session, state, (TEvent)@event).Wait();
            }
            session.Store(state);
        }

        public override async Task HandleAsync(IDocumentSession session, TAggregate state, object @event)
        {
            //TODO: Maybe a bit more of defensive programmming instead straight typecasting?
            await Apply(session, state, (TEvent)@event);
            session.Store(state);
        }

        public static ProjectionCreateOrUpdateEventHandler<TAggregate, TEvent> Create(
            bool onlyUpdate,
            Func<IDocumentSession, TAggregate, TEvent, Task> apply
        )
        {
            return new ProjectionCreateOrUpdateEventHandler<TAggregate, TEvent>(onlyUpdate, apply);
        }
    }
}
