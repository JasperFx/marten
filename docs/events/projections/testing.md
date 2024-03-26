# Testing Projections

So you're using Marten, you've embraced event sourcing and projections, and now you'd like to write some tests against your
projection code. By and large, I think the Marten team would recommend to use integration ("social") testing as much as possible
and test your projection code through Marten itself so you can feel confident that your projection code will work correctly
in production.

For the moment, consider this single stream projection that builds up a simplistic `Invoice` document from a stream of 
related events:

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

## Live Aggregation

For projections that are running with an `Async` lifecycle, you can at least test single stream projections through the
`AggregateStreamAsync()` behavior as shown below:

<!-- snippet: sample_testing_live_projection -->
<a id='snippet-sample_testing_live_projection'></a>
```cs
[Fact]
public async Task test_live_aggregation()
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(
            "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5");
        opts.DatabaseSchemaName = "incidents";
    });

    var invoiceId = Guid.NewGuid();

    // Pump in events
    using (var session = store.LightweightSession())
    {

        session.Events.StartStream<Invoice>(invoiceId, new InvoiceCreated("Blue Shoes", 112.24m));
        await session.SaveChangesAsync();

        session.Events.Append(invoiceId,new InvoiceApproved());
        session.Events.Append(invoiceId,new InvoicePaid());
        await session.SaveChangesAsync();
    }

    await using var query = store.QuerySession();

    var invoice = await query.Events.AggregateStreamAsync<Invoice>(invoiceId);
    invoice.Description.ShouldBe("Blue Shoes");
    invoice.Status.ShouldBe(InvoiceStatus.Paid);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L79-L112' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_testing_live_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Inline Aggregation

For projections that are running with an `Inline` lifecycle, you can test any projection by pumping in events, then
loading the newly persisted documents from the database like so:

<!-- snippet: sample_testing_inline_aggregation -->
<a id='snippet-sample_testing_inline_aggregation'></a>
```cs
[Fact]
public async Task test_inline_aggregation()
{
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(
            "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5");
        opts.DatabaseSchemaName = "incidents";

        // Notice that the "snapshot" is running inline
        opts.Projections.Snapshot<Invoice>(SnapshotLifecycle.Inline);
    });

    var invoiceId = Guid.NewGuid();

    // Pump in events
    using (var session = store.LightweightSession())
    {

        session.Events.StartStream<Invoice>(invoiceId, new InvoiceCreated("Blue Shoes", 112.24m));
        await session.SaveChangesAsync();

        session.Events.Append(invoiceId,new InvoiceApproved());
        session.Events.Append(invoiceId,new InvoicePaid());
        await session.SaveChangesAsync();
    }

    await using var query = store.QuerySession();

    // Load the document that was "projected" from the events above
    // and immediately persisted to the document store
    var invoice = await query.LoadAsync<Invoice>(invoiceId);

    // Run assertions
    invoice.Description.ShouldBe("Blue Shoes");
    invoice.Status.ShouldBe(InvoiceStatus.Paid);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L115-L155' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_testing_inline_aggregation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Async Projections

For asynchronous projections of any kind, we have a little more complicated situation. We can still pump in events through
Marten as normal to establish the inputs to our test (the "arrange" part of the arrange/act/assert cycle). The challenge with asynchronous
projections is to "know" when the asynchronous daemon running in the 
background has progressed past the events so that it's accurate to check the expected outcome by loading persisted documents
from the database. 

A simple, but potentially expensive approach would be to use the async daemon to rebuild a projection after appending the events
so you "know" the test assertions are running after the daemon has caught up. Here's an example of that approach:

<!-- snippet: sample_simple_test_of_async_aggregation -->
<a id='snippet-sample_simple_test_of_async_aggregation'></a>
```cs
[Fact]
public async Task test_async_aggregation()
{
    // By building the Marten store this way, there is **no** projection daemon running
    // yet.
    using var store = DocumentStore.For(opts =>
    {
        opts.Connection(
            "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5");
        opts.DatabaseSchemaName = "incidents";

        // Notice that the "snapshot" is running inline
        opts.Projections.Snapshot<Invoice>(SnapshotLifecycle.Async);
    });

    await store.Advanced.Clean.DeleteAllEventDataAsync();
    await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Invoice));

    var invoiceId = Guid.NewGuid();

    // Pump in events
    using (var session = store.LightweightSession())
    {
        session.Events.StartStream<Invoice>(invoiceId, new InvoiceCreated("Blue Shoes", 112.24m));
        await session.SaveChangesAsync();

        session.Events.Append(invoiceId,new InvoiceApproved());
        session.Events.Append(invoiceId,new InvoicePaid());
        await session.SaveChangesAsync();
    }

    // Here I'm going to completely rewind the async projections, then
    // rebuild from 0 to the very end of the event store so we know
    // we got our new stream from up above completely processed
    using var daemon = await store.BuildProjectionDaemonAsync();
    await daemon.RebuildProjectionAsync<Invoice>(CancellationToken.None);

    // NOW, we should expect reliable results by just loading the already
    // persisted documents built by rebuilding the projection
    await using var query = store.QuerySession();

    // Load the document that was "projected" from the events above
    // and immediately persisted to the document store
    var invoice = await query.LoadAsync<Invoice>(invoiceId);

    // Run assertions
    invoice.Description.ShouldBe("Blue Shoes");
    invoice.Status.ShouldBe(InvoiceStatus.Paid);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L157-L209' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simple_test_of_async_aggregation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The approach above is pretty simple, but it definitely works against your ability to parallelize tests by rewinding the 
existing projection. It also might become slower over time as you accumulate more and more events and `Invoice` data from
prior runs. You *could* beat that issue by cleaning off the database before doing the arrange, act, and assert cycle with these lines 
of code:

```csharp
        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Invoice));
```

::: warning
The `WaitForNonStaleProjectionDataAsync(timeout)` can only work on one database at a time. If you are using some kind 
of multi-tenancy with separate databases per tenant, there is an overload that takes in a tenant id or database name.
You will need to use that overload separately for any impacted databases in your tests if using multi-tenancy through
separate databases.
:::

Or starting with Marten 7.5, you can just use an already running async daemon, but force the test harness to "wait" for
the asynchronous daemon to completely catch up to the latest event captured for all projections with the `WaitForNonStaleProjectionDataAsync(timeout)` API. Using that approach
gives us this test:

<!-- snippet: sample_using_wait_for_non_stale_data -->
<a id='snippet-sample_using_wait_for_non_stale_data'></a>
```cs
[Fact]
public async Task test_async_aggregation_with_wait_for()
{
    // In your tests, you would most likely use the IHost for your
    // application as it is normally built
    using var host = await Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(
                        "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5");
                    opts.DatabaseSchemaName = "incidents";

                    // Notice that the "snapshot" is running inline
                    opts.Projections.Snapshot<Invoice>(SnapshotLifecycle.Async);
                })

                // Using Solo in tests will help it start up a little quicker
                .AddAsyncDaemon(DaemonMode.Solo);
        }).StartAsync();

    var store = host.Services.GetRequiredService<IDocumentStore>();

    var invoiceId = Guid.NewGuid();

    // Pump in events
    using (var session = store.LightweightSession())
    {
        session.Events.StartStream<Invoice>(invoiceId, new InvoiceCreated("Blue Shoes", 112.24m));
        await session.SaveChangesAsync();

        session.Events.Append(invoiceId,new InvoiceApproved());
        session.Events.Append(invoiceId,new InvoicePaid());
        await session.SaveChangesAsync();
    }

    // Now, this is going to pause here in this thread until the async daemon
    // running in our IHost is completely caught up to at least the point of the
    // last event captured at the point this method was called
    await store.WaitForNonStaleProjectionDataAsync(5.Seconds());

    // NOW, we should expect reliable results by just loading the already
    // persisted documents built by rebuilding the projection
    await using var query = store.QuerySession();

    // Load the document that was "projected" from the events above
    // and immediately persisted to the document store
    var invoice = await query.LoadAsync<Invoice>(invoiceId);

    // Run assertions
    invoice.Description.ShouldBe("Blue Shoes");
    invoice.Status.ShouldBe(InvoiceStatus.Paid);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L211-L268' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_wait_for_non_stale_data' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the version above, we can just be using a shared `IHost` and the async daemon already running continuously, pump in 
new events, then force the test harness to "wait" for the underlying async daemon to be completely caught up before proceeding
to test the expected documents persisted in the database by the projection. 

## What about System Time?!?

::: info
See Andrew Lock's blog post [Avoiding flaky tests with TimeProvider and ITimer](https://andrewlock.net/exploring-the-dotnet-8-preview-avoiding-flaky-tests-with-timeprovider-and-itimer/) for more information on using `TimeProvider` in tests
:::

In the example projection, I've been capturing the timestamp in the `Invoice` document from the Marten event metadata:

<!-- snippet: sample_using_event_metadata_in_Invoice -->
<a id='snippet-sample_using_event_metadata_in_invoice'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L42-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_event_metadata_in_invoice' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

But of course, if that timestamp has some meaning later on and you have any kind of business rules that may need to key
off that time, it's very helpful to be able to control the timestamps that Marten is assigning to create predictable
automated tests. As of Marten 7.5, Marten uses the newer .NET [TimeProvider](https://learn.microsoft.com/en-us/dotnet/api/system.timeprovider?view=net-8.0) behind the scenes, and you can replace it in 
testing like so:

<!-- snippet: sample_using_fake_time_provider -->
<a id='snippet-sample_using_fake_time_provider'></a>
```cs
[Fact]
public async Task test_async_aggregation_with_wait_for_and_fake_time_provider()
{
    // Hang on to this for later!!!
    var eventsTimeProvider = new FakeTimeProvider();

    // In your tests, you would most likely use the IHost for your
    // application as it is normally built
    using var host = await Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(
                        "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5");
                    opts.DatabaseSchemaName = "incidents";

                    // Notice that the "snapshot" is running inline
                    opts.Projections.Snapshot<Invoice>(SnapshotLifecycle.Async);

                    opts.Events.TimeProvider = eventsTimeProvider;
                })

                // Using Solo in tests will help it start up a little quicker
                .AddAsyncDaemon(DaemonMode.Solo);
        }).StartAsync();

    var store = host.Services.GetRequiredService<IDocumentStore>();

    var invoiceId = Guid.NewGuid();

    // Pump in events
    using (var session = store.LightweightSession())
    {
        session.Events.StartStream<Invoice>(invoiceId, new InvoiceCreated("Blue Shoes", 112.24m));
        await session.SaveChangesAsync();

        session.Events.Append(invoiceId,new InvoiceApproved());
        session.Events.Append(invoiceId,new InvoicePaid());
        await session.SaveChangesAsync();
    }

    // Now, this is going to pause here in this thread until the async daemon
    // running in our IHost is completely caught up to at least the point of the
    // last event captured at the point this method was called
    await store.WaitForNonStaleProjectionDataAsync(5.Seconds());

    // NOW, we should expect reliable results by just loading the already
    // persisted documents built by rebuilding the projection
    await using var query = store.QuerySession();

    // Load the document that was "projected" from the events above
    // and immediately persisted to the document store
    var invoice = await query.LoadAsync<Invoice>(invoiceId);

    // Run assertions, and we'll use the faked timestamp
    // from our time provider
    invoice.Created.ShouldBe(eventsTimeProvider.Start);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/testing_projections.cs#L271-L333' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_fake_time_provider' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In the sample above, I used the `FakeTimeProvider` from the Microsoft.Extensions.TimeProvider.Testing Nuget package.