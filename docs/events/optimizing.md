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
    // of database queries necessary to process inline projections
    // during command handling with some significant
    // caveats
    opts.Events.UseIdentityMapForInlineAggregates = true;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/Optimizations.cs#L31-L54' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_turn_on_optimizations_for_event_sourcing' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The archived stream option is further described in the section on [Hot/Cold Storage Partitioning](/events/archiving.html#hot-cold-storage-partitioning).

See the ["Rich" vs "Quick" Appends](/events/appending.html#rich-vs-quick-appends) section for more information about the
applicability and drawbacks of the "Quick" event appending.

Lastly, see [Optimizing FetchForWriting with Inline Aggregates](/scenarios/command_handler_workflow.html#optimizing-fetchforwriting-with-inline-aggregates) for more information
about the `UseIdentityMapForInlineAggregates` option.
