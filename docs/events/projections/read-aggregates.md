# Reading Aggregates 

::: info
The only "special" aspect of a single stream projection document storage is that Marten
forces you to use numerical revisioning and does generate ever so slightly different PostgreSQL
functions to update the document that tie the revisioning checks and results to the stream version.

Multi-stream projections have the same revisioning logic without any other customization.
:::

::: tip
Don't manually change the projection data without changing the event data. Or at least try
really hard *not* to do that.
:::

If an aggregated projection is persisted through either the `Inline` or `Async` lifecycles,
that data is stored as "just" a regular old [Marten document](/documents/). This means that you
can use any bit of Marten functionality to query or load projected documents including the LINQ
support. 

Do note thought that `Async` projections give you [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency).
With some necessary caution as this can time out or lead to slower response times, you can get
consistent data from Marten asynchronous projections through this usage:

<!-- snippet: sample_querying_for_non_stale_projection_data -->
<a id='snippet-sample_querying_for_non_stale_projection_data'></a>
```cs
// theSession is an IDocumentSession
var summaries = await theSession
    // This makes Marten "wait" until the async daemon progress for whatever projection
    // is building the BoardSummary document to catch up to the point at which the
    // event store was at when you first tired to execute the LINQ query
    .QueryForNonStaleData<BoardSummary>(10.Seconds())
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DaemonTests/Composites/multi_stage_projections.cs#L235-L245' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_querying_for_non_stale_projection_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To be clear though, if you need the latest version of a single stream projection, we recommend always
using the `FetchLatest` API described in the next section. Anytime you need to use a the data from
a single stream projection as a "write model" in a command handler where you may need to write
new events, we *strongly* recommend using the [`FetchForWriting`](/scenarios/command_handler_workflow) API.

## FetchLatest <Badge type="tip" text="7.34" />

::: tip
`FetchLatest` is a little more lightweight in execution than `FetchForWriting` and
should be used if all you care about is read only data without appending new events.

If you are serving HTTP APIs, see [Writing Event Sourcing Aggregates](/documents/aspnetcore#writing-event-sourcing-aggregates)
for how to stream the aggregate's JSON directly to the HTTP response with zero deserialization using `WriteLatest<T>()`.
:::

::: warning
For internal reasons, the `FetchLatest()` API is only available off of `IDocumentSession` and not `IQuerySession`.
:::

But wait, there's a way to both get a guarantee of getting the exact correct information about an `Invoice`
for the current event data that works no matter what projection lifecycle we're running the
`Invoice` aggregate? Marten now has the singular `FetchLatest()` API to do exactly that:

<!-- snippet: sample_read_latest_invoice -->
<a id='snippet-sample_read_latest_invoice'></a>
```cs
public static async Task read_latest(
    // Watch this, only available on the full IDocumentSession
    IDocumentSession session,
    Guid invoiceId)
{
    var invoice = await session
        .Events.FetchLatest<Projections.Invoice>(invoiceId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L89-L100' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_read_latest_invoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Just to understand how this API works, under the covers, if `Invoice` is registered as:

1. `Live`, then `FetchLatest()` is basically doing the same thing as `AggregateStreamAsync()`
2. `Inline`, then `FetchLatest()` is essentially using `LoadAsync()`
3. `Async`, then `FetchLatest()` does a little bit more. It queries both the for the current snapshot of the `Invoice`, then any
   events for that `Invoice` that haven't yet been applied, and advances the `Invoice` in memory so that you get the exact
   current state of the `Invoice` even if the async daemon process is behind the latest changes

Moreover, `FetchLatest` was meant to be used in conjunction with `FetchForWriting()` to get you the most
current version of an aggregate that was just updated using `FetchForWriting()` from the same session. To really
get the most of this combination, use this opt in flag:

<!-- snippet: sample_configure_aggregate_with_optimizations -->
<a id='snippet-sample_configure_aggregate_with_optimizations'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // This opts Marten into a pretty big optimization
    // for how FetchForWriting and/or FetchLatest work internally
    opts.Events.UseIdentityMapForAggregates = true;
    opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L104-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_with_optimizations' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
That flag is `false` by default because it was introduced halfway through the 7.* version lifecycle,
and can introduce subtle bugs in application code if you use some kind of `AggregateRoot` pattern where
your application code mutates the aggregate projection objects outside of Marten control.

Also, the Marten team recommends an approach where only Marten itself ever changes the state of a projected document
and you keep application logic separate from the projected data classes. More or less, we're recommending more of a
functional programming approach.
:::

Now, let's say that in our commands we want to both mutate an `Invoice` event stream by appending new events *and*
return the newly updated state of the `Invoice` to the original caller in the most efficient way possible. Just for
fun, let's say we wrote a helper function like this:

<!-- snippet: sample_mutation_extensions -->
<a id='snippet-sample_mutation_extensions'></a>
```cs
public static class MutationExtensions
{
    public static async Task<Projections.Invoice> MutateInvoice(this IDocumentSession session, Guid id, Func<Projections.Invoice, IEnumerable<object>> decider,
        CancellationToken token = default)
    {
        var stream = await session.Events.FetchForWriting<Projections.Invoice>(id, token);

        // Decide what new events should be appended based on the current
        // state of the aggregate and application logic
        var events = decider(stream.Aggregate);
        stream.AppendMany(events);

        // Persist any new events
        await session.SaveChangesAsync(token);

        return await session.Events.FetchLatest<Projections.Invoice>(id, token);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L138-L159' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mutation_extensions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And used it for a command handler something like this:

<!-- snippet: sample_invoice_approval_workflow_with_mutate -->
<a id='snippet-sample_invoice_approval_workflow_with_mutate'></a>
```cs
public static Task Approve(IDocumentSession session, Guid invoiceId)
{
    return session.MutateInvoice(invoiceId, invoice =>
    {
        if (invoice.Status != InvoiceStatus.Approved)
        {
            return [new InvoiceApproved()];
        }

        return [];
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L120-L135' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_invoice_approval_workflow_with_mutate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Okay, so for some context, if using the full fledged `UseIdentityMapForAggregates` + `FetchForWriting`, then `FetchLatest`
workflow, Marten is optimizing the `FetchLatest` if the lifecycle is:

1. `Live`, then Marten starts with the version of the aggregate `Invoice` created by the initial `FetchForWriting()` call
   and applies any new events appended in that operation to the `Invoice` to create the "latest" version for you without
   incurring any additional database round trips
2. `Inline`, then Marten will add the initially loaded `Invoice` from `FetchForWriting` into the identity map
   for the session *regardless* of what type of session this is, and `FetchLatest` will use the value of the
   projected `Invoice` updated as part of `SaveChangesAsync()` to prevent any additional database round trips
3. `Async`, then Marten will use the initial version of the `Invoice` aggregate loaded by `FetchForWriting()` and
   applies with any additional events appended to that session to give you the exact version of the `Invoice` after
   the new events are applied

In all cases, the `FetchForWriting` + `FetchLatest` combination is working together to get you
the correct information in the most efficient way possible by eliminating extra trips to the
database.

## Live Aggregation

Also see [/events/projections/live-aggregates]
