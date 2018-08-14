<!--Title: Projecting by Event Type -->

While projections can target specific stream or streams, it is also possible to project by event types. The following sample demonstrates this with the `CandleProjection` that implements the `ViewProjection` interface to build `Candle` projections from events of type `Tick`.

Introduce a type to hold candle data:

<[sample:sample-type-candle]>

This data will then be populated and updated from observing ticks:

<[sample:sample-type-tick]>

We then introduce a projection that subscribes to the 'Tick' event:

<[sample:sample-candleprojection]>

Lastly, we configure the Event Store to use the newly introduced projection:

<[sample:sample-project-by-event-type]> 
 