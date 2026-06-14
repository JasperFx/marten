using System;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples;

#region sample_projection_by_event_type_candle
public class Candle
{
    public Guid Id { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
#endregion

#region sample_projection_by_event_type_tick
// Each Tick carries the identity of the candle it contributes to. The
// ticks can be captured in any number of separate event streams.
public record Tick(Guid CandleId, decimal Price);
#endregion

#region sample_projection_by_event_type_projection
public partial class CandleProjection: MultiStreamProjection<Candle, Guid>
{
    public CandleProjection()
    {
        // Group every Tick event by its CandleId so that all ticks for the
        // same candle are aggregated together, regardless of which stream
        // each Tick was captured in. The projection only reacts to events
        // of type Tick.
        Identity<Tick>(x => x.CandleId);
    }

    public void Apply(Candle candle, Tick tick)
    {
        if (candle.Open == 0)
        {
            candle.Open = tick.Price;
        }

        candle.High = candle.High == 0 ? tick.Price : Math.Max(candle.High, tick.Price);
        candle.Low = candle.Low == 0 ? tick.Price : Math.Min(candle.Low, tick.Price);
        candle.Close = tick.Price;
    }
}
#endregion

public static class CandleProjectionRegistration
{
    public static void configure()
    {
        #region sample_projection_by_event_type_registration
        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Register the projection by event type
            opts.Projections.Add<CandleProjection>(ProjectionLifecycle.Inline);
        });
        #endregion
    }
}
