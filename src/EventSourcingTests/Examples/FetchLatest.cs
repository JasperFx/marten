using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public static class FetchLatest
{
    public static void configure_live()
    {
        #region sample_configure_aggregate_as_live

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

        #endregion
    }

    #region sample_read_live_invoice

    public static async Task read_live_invoice(
        IQuerySession session,
        Guid invoiceId)
    {
        var invoice = await session
            .Events.AggregateStreamAsync<Invoice>(invoiceId);
    }

    #endregion

    public static void configure_inline()
    {
        #region sample_configure_aggregate_as_inline

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Inline);
        });

        #endregion
    }

    #region sample_read_inline_invoice

    public static async Task read_inline_invoice(
        IQuerySession session,
        Guid invoiceId)
    {
        var invoice = await session
            .LoadAsync<Invoice>(invoiceId);
    }

    #endregion

    public static void configure_async()
    {
        #region sample_configure_aggregate_as_async

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
            {
                opts.Connection(builder.Configuration.GetConnectionString("marten"));

                opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Async);
            })
            .AddAsyncDaemon(DaemonMode.HotCold);

        #endregion
    }

    #region sample_read_latest_invoice

    public static async Task read_latest(
        // Watch this, only available on the full IDocumentSession
        IDocumentSession session,
        Guid invoiceId)
    {
        var invoice = await session
            .Events.FetchLatest<Projections.Invoice>(invoiceId);
    }

    #endregion

    public static void configure_with_optimizations()
    {
        #region sample_configure_aggregate_with_optimizations

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // This opts Marten into a pretty big optimization
            // for how FetchForWriting and/or FetchLatest work internally
            opts.Events.UseIdentityMapForAggregates = true;
            opts.Projections.Snapshot<Projections.Invoice>(SnapshotLifecycle.Inline);
        });

        #endregion
    }

    #region sample_invoice_approval_workflow_with_mutate

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

    #endregion
}

#region sample_mutation_extensions

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

#endregion
