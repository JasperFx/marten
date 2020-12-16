using System;

namespace Marten.Events.Projections
{
    // SAMPLE: ITransform
    [Obsolete("This will be eliminated in V4 and replaced w/ the new ViewProjection")]
    public interface ITransform<TEvent, TView>
    {
        TView Transform(StreamAction stream, Event<TEvent> input);
    }

    // ENDSAMPLE
}
