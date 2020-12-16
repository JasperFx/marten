using System;

namespace Marten.Events.Projections
{
    [Obsolete("This will be eliminated in V4")]
    public interface IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, TEvent @event);
    }

    [Obsolete("This will be eliminated in V4")]
    public interface IAggregationWithMetadata<TAggregate, TEvent>: IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, Event<TEvent> @event);
    }
}
