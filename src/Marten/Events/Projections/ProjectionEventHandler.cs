using System;
using System.Linq;

namespace Marten.Events.Projections
{
    internal interface IProjectionEventHandler<TAggregate>
    {
        Type[] Handles { get; }

        void Handle(IDocumentSession session, TAggregate state, IEvent @event);

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

        public abstract void Handle(IDocumentSession session, TAggregate state, IEvent @event);
    }

    internal class ProjectionDeleteEventHandler<TAggregate>: ProjectionEventHandler<TAggregate> where TAggregate : class, new()
    {
        public override Type[] Handles => handles;

        public readonly Type[] handles;

        public ProjectionDeleteEventHandler(params Type[] handles)
        {
            this.handles = handles;
        }

        public override void Handle(IDocumentSession session, TAggregate state, IEvent @event)
        {
            session.Delete(state);
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

        public override void Handle(IDocumentSession session, TAggregate state, IEvent @event)
        {
            @event.Apply(state, aggregator);
            session.Store(state);
        }
    }
}
