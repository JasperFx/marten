using Marten.Events.Projections;

namespace ModularConfigTests.SatelliteB;

// `partial` required for the JasperFx.Events.SourceGenerator to emit the
// dispatcher partial-class merge. See OrderProjection in SatelliteA for
// the post-#276 SG-only contract.
public partial class DailyProjection : MultiStreamProjection<Daily, string>
{
    public DailyProjection()
    {
        // Multi-stream keyer: both event types route to the Daily whose
        // Id matches the event's Day string. Lets events from many source
        // streams contribute to one shared daily counter.
        Identity<DailyOpened>(x => x.Day);
        Identity<DailyClosed>(x => x.Day);
    }

    public void Apply(DailyOpened @event, Daily snapshot)
    {
        snapshot.OpenCount++;
    }

    public void Apply(DailyClosed @event, Daily snapshot)
    {
        snapshot.CloseCount++;
    }
}
