namespace Marten.Events.Projections
{
    // SAMPLE: ITransform
    public interface ITransform<TEvent, TView>
    {
        TView Transform(StreamAction stream, Event<TEvent> input);
    }

    // ENDSAMPLE
}
