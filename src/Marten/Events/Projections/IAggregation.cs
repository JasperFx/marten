namespace Marten.Events.Projections
{
    public interface IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, TEvent @event);
    }

    public interface IAggregationWithMetadata<TAggregate, TEvent> : IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, Event<TEvent> @event);
    }
}