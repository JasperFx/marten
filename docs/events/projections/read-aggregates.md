# Reading Aggregates 

One of the primary use cases for projections with Marten in day to day development is going to be needing
to read current state of a single event stream as an aggregate (what Marten calls a "single stream projection").

Let's say we have an aggregate for an `Invoice` in our system that we use to create a "write" or "read" model of 
a single invoice event stream in our system like so:

<!-- snippet: sample_simplistic_invoice_projection -->
<a id='snippet-sample_simplistic_invoice_projection'></a>
```cs
public record InvoiceCreated(string Description, decimal Amount);

public record InvoiceApproved;
public record InvoiceCancelled;
public record InvoicePaid;
public record InvoiceRejected;

public class Invoice
{
    public Invoice()
    {
    }

    public static Invoice Create(IEvent<InvoiceCreated> created)
    {
        return new Invoice
        {
            Amount = created.Data.Amount,
            Description = created.Data.Description,

            // Capture the timestamp from the event
            // metadata captured by Marten
            Created = created.Timestamp,
            Status = InvoiceStatus.Created
        };
    }

    public int Version { get; set; }

    public decimal Amount { get; set; }
    public string Description { get; set; }
    public Guid Id { get; set; }
    public DateTimeOffset Created { get; set; }
    public InvoiceStatus Status { get; set; }

    public void Apply(InvoiceCancelled _) => Status = InvoiceStatus.Cancelled;
    public void Apply(InvoiceRejected _) => Status = InvoiceStatus.Rejected;
    public void Apply(InvoicePaid _) => Status = InvoiceStatus.Paid;
    public void Apply(InvoiceApproved _) => Status = InvoiceStatus.Approved;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L27-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simplistic_invoice_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If we were to register that `Invoice` aggregate as a `Live` snapshot like so:

<!-- snippet: sample_configure_aggregate_as_live -->
<a id='snippet-sample_configure_aggregate_as_live'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Just telling Marten upfront that we will use
    // live aggregation for the Invoice aggregate
    // This would be the default anyway if you didn't explicitly
    // register Invoice this way, but doing so let's
    // Marten "know" about Invoice for code generation
    opts.Projections.LiveStreamAggregation<Projections.Invoice>();
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L18-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_as_live' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then we could use the `AggregateStreamAsync` API to read the current `Invoice` state for any
single event stream like so:

<!-- snippet: sample_read_live_invoice -->
<a id='snippet-sample_read_live_invoice'></a>
```cs
public static async Task read_live_invoice(
    IQuerySession session,
    Guid invoiceId)
{
    var invoice = await session
        .Events.AggregateStreamAsync<Invoice>(invoiceId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L36-L46' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_read_live_invoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: info
`AggregateStreamAsync()` will work regardless of the registered projection lifecycle, and is also your
primary mechanism for "time travel" querying of projection state.
:::

If instead, we wanted strong consistency and would prefer to update our `Invoice` aggregates as an
`Inline` snapshot:

<!-- snippet: sample_configure_aggregate_as_inline -->
<a id='snippet-sample_configure_aggregate_as_inline'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L50-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_as_inline' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then we can just treat the `Invoice` as any old Marten document (because it is) and use
the standard `LoadAsync()` API to load the current state of an `Invoice` for an event stream like:

<!-- snippet: sample_read_inline_invoice -->
<a id='snippet-sample_read_inline_invoice'></a>
```cs
public static async Task read_inline_invoice(
    IQuerySession session,
    Guid invoiceId)
{
    var invoice = await session
        .LoadAsync<Invoice>(invoiceId);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L63-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_read_inline_invoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And lastly, if we wanted to run the `Invoice` snapshot updates as an asynchronous projection (maybe to take advantage
of Marten's ability to do blue/green deployments?):

<!-- snippet: sample_configure_aggregate_as_async -->
<a id='snippet-sample_configure_aggregate_as_async'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("marten"));

        opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Async);
    })
    .AddAsyncDaemon(DaemonMode.HotCold);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L77-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_as_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We would still just the same `LoadAsync()` API, but you just hope that 
the async daemon has caught up to where ever our particular `Invoice` was last updated.

::: tip
Ah, the joys of [eventual consistency](https://en.wikipedia.org/wiki/Eventual_consistency). 
:::

<!-- snippet: sample_configure_aggregate_as_inline -->
<a id='snippet-sample_configure_aggregate_as_inline'></a>
```cs
var builder = Host.CreateApplicationBuilder();
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Inline);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L50-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_as_inline' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## FetchLatest <Badge type="tip" text="7.34" />

::: tip
`FetchLatest` is a little more lightweight in execution than `FetchForWriting` and
should be used if all you care about is read only data without appending new events.
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L91-L102' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_read_latest_invoice' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L106-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_configure_aggregate_with_optimizations' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L140-L161' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_mutation_extensions' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/FetchLatest.cs#L122-L137' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_invoice_approval_workflow_with_mutate' title='Start of snippet'>anchor</a></sup>
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
