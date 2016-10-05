namespace Marten.Events.Projections
{
    public interface IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, Event<TEvent> @event);
    }
}