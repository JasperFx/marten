# Projecting by Event Type

While projections can target a specific stream or streams, it is also possible to project by event type. The following sample demonstrates this with a `CandleProjection` that extends `MultiStreamProjection<Candle, Guid>` to build `Candle` aggregates from events of type `Tick`, grouping every `Tick` by its `CandleId` regardless of which stream the event was captured in.

Introduce a type to hold candle data:

<!-- snippet: sample_projection_by_event_type_candle -->
<a id='snippet-sample_projection_by_event_type_candle'></a>
```cs
public class Candle
{
    public Guid Id { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/CandleProjectionSample.cs#L8-L17' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_projection_by_event_type_candle' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This data will then be populated and updated from observing ticks:

<!-- snippet: sample_projection_by_event_type_tick -->
<a id='snippet-sample_projection_by_event_type_tick'></a>
```cs
// Each Tick carries the identity of the candle it contributes to. The
// ticks can be captured in any number of separate event streams.
public record Tick(Guid CandleId, decimal Price);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/CandleProjectionSample.cs#L19-L23' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_projection_by_event_type_tick' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We then introduce a projection that subscribes to the `Tick` event:

<!-- snippet: sample_projection_by_event_type_projection -->
<a id='snippet-sample_projection_by_event_type_projection'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/CandleProjectionSample.cs#L25-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_projection_by_event_type_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, we configure the event store to use the newly introduced projection:

<!-- snippet: sample_projection_by_event_type_registration -->
<a id='snippet-sample_projection_by_event_type_registration'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Register the projection by event type
    opts.Projections.Add<CandleProjection>(ProjectionLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/CandleProjectionSample.cs#L55-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_projection_by_event_type_registration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
