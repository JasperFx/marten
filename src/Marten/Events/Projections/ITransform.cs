namespace Marten.Events.Projections
{
    // SAMPLE: ITransform
    public interface ITransform<TEvent, TView>
    {
        TView Transform(EventStream stream, Event<TEvent> input);
    }

    // ENDSAMPLE
}
