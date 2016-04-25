namespace Marten.Events.Projections
{
    public interface ITransform<TEvent, TView>
    {
        TView Transform(Event<TEvent> input);
    }
}