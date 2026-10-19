using System.Threading;
using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;


namespace EventSourcingTests.Examples;

public class EventSkipping
{
    public static void enable_event_skipping()
    {
        #region sample_enabling_event_skipping

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // This is false by default for backwards compatibility,
            // turning this on will add an extra column and filtering during
            // various event store operations
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        });

        #endregion
    }

    #region sample_mark_events_as_skipped
    public static async Task mark_events_as_skipped(
        IDocumentStore store,
        long[] sequences,
        CancellationToken cancellation)
    {
        await store.Storage.Database.MarkEventsAsSkipped(sequences, cancellation);
    }
    #endregion

    #region sample_mark_events_as_skipped_by_tenant
    public static async Task mark_events_as_skipped_by_tenant(
        IDocumentStore store,
        long[] sequences,
        string tenantId,
        CancellationToken cancellation)
    {
        // If you're using multi-tenancy through separate databases,
        // you'll need to use the correct database
        var database = await store.Storage.FindOrCreateDatabase(tenantId);

        // And name the tenant. With conjoined tenancy and
        // UseTenantPartitionedEvents, every tenant numbers its own
        // events, so a sequence alone matches the same-numbered
        // event of every other tenant
        await database.MarkEventsAsSkipped(tenantId, sequences, cancellation);
    }
    #endregion

}
