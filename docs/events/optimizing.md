# Optimizing for Performance and Scalability <Badge type="tip" text="7.25" />

::: tip
The asynchronous projection and subscription support can in some cases suffer some event "skipping" when transactions
that are appending transactions become slower than the `StoreOptions.Projections.StaleSequenceThreshold` (the default is only 3 seconds).

From initial testing, the `Quick` append mode seems to stop this problem altogether. This only seems to be an issue with 
very large data loads.
:::

Marten has several options to potentially increase the performance and scalability of a system that uses
the event sourcing functionality:

<!-- snippet: sample_turn_on_optimizations_for_event_sourcing -->
<a id='snippet-sample_turn_on_optimizations_for_event_sourcing'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection("some connection string");

    // Turn on the PostgreSQL table partitioning for
    // hot/cold storage on archived events
    opts.Events.UseArchivedStreamPartitioning = true;

    // Use the *much* faster workflow for appending events
    // at the cost of *some* loss of metadata usage for
    // inline projections
    opts.Events.AppendMode = EventAppendMode.Quick;

    // Little more involved, but this can reduce the number
    // of database queries necessary to process projections
    // during CQRS command handling with certain workflows
    opts.Events.UseIdentityMapForAggregates = true;

    // Opts into a mode where Marten is able to rebuild single
    // stream projections faster by building one stream at a time
    // Does require new table migrations for Marten 7 users though
    opts.Events.UseOptimizedProjectionRebuilds = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L31-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_optimizations_for_event_sourcing' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The archived stream option is further described in the section on [Hot/Cold Storage Partitioning](/events/archiving.html#hot-cold-storage-partitioning).

See the ["Rich" vs "Quick" Appends](/events/appending.html#rich-vs-quick-appends) section for more information about the
applicability and drawbacks of the "Quick" event appending.

See [Optimizing FetchForWriting with Inline Aggregates](/scenarios/command_handler_workflow.html#optimizing-fetchforwriting-with-inline-aggregates) for more information
about the `UseIdentityMapForInlineAggregates` option.

Lastly, check out [Optimized Projection Rebuilds](/events/projections/rebuilding.html#optimized-projection-rebuilds) for information about `UseOptimizedProjectionRebuilds`

## Caching for Asynchronous Projections

You may be able to wring out more throughput for aggregated projections (`SingleStreamProjection`, `MultiStreamProjection`, `CustomProjection`)
by opting into 2nd level caching of the aggregated projected documents during asynchronous projection building. You can
do that by setting a greater than zero value for `CacheLimitPerTenant` directly inside of the aforementioned projection types
like so:

<!-- snippet: sample_showing_fanout_rules -->
<a id='snippet-sample_showing_fanout_rules'></a>
```cs
public class DayProjection: MultiStreamProjection<Day, int>
{
    public DayProjection()
    {
        // Tell the projection how to group the events
        // by Day document
        Identity<IDayEvent>(x => x.Day);

        // This just lets the projection work independently
        // on each Movement child of the Travel event
        // as if it were its own event
        FanOut<Travel, Movement>(x => x.Movements);

        // You can also access Event data
        FanOut<Travel, Stop>(x => x.Data.Stops);

        ProjectionName = "Day";

        // Opt into 2nd level caching of up to 100
        // most recently encountered aggregates as a
        // performance optimization
        CacheLimitPerTenant = 1000;

        // With large event stores of relatively small
        // event objects, moving this number up from the
        // default can greatly improve throughput and especially
        // improve projection rebuild times
        Options.BatchSize = 5000;
    }

    public void Apply(Day day, TripStarted e) => day.Started++;
    public void Apply(Day day, TripEnded e) => day.Ended++;

    public void Apply(Day day, Movement e)
    {
        switch (e.Direction)
        {
            case Direction.East:
                day.East += e.Distance;
                break;
            case Direction.North:
                day.North += e.Distance;
                break;
            case Direction.South:
                day.South += e.Distance;
                break;
            case Direction.West:
                day.West += e.Distance;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void Apply(Day day, Stop e) => day.Stops++;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/ViewProjectionTests.cs#L132-L192' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_showing_fanout_rules' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Marten is using a most recently used cache for the projected documents that are being built by an aggregation projection
so that updates from new events can be directly applied to the in memory documents instead of having to constantly
load those documents over and over again from the database as new events trickle in. This is of course much more effective
when your projection is constantly updating a relatively small number of different aggregates.
