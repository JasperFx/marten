using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public enum InvoiceStatus
{
    Created,
    Approved,
    Cancelled,
    Rejected,
    Paid
}

#region sample_simplistic_invoice_projection

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

    #region sample_using_event_metadata_in_Invoice

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

    #endregion

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

#endregion


public class testing_projections
{
    #region sample_testing_live_projection

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

    #endregion


    #region sample_testing_inline_aggregation

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

    #endregion

    #region sample_simple_test_of_async_aggregation

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

    #endregion

    #region sample_using_wait_for_non_stale_data

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

    #endregion


    #region sample_using_fake_time_provider

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

    #endregion
}
