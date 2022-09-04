using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Schema;
using Microsoft.Extensions.Hosting;

namespace CoreTests.Examples;

public class MultipleDocumentStores
{
    public static async Task bootstrap()
    {
        #region sample_bootstrapping_separate_Store

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // You can still use AddMarten() for the main document store
                // of this application
                services.AddMarten("some connection string");

                services.AddMartenStore<IInvoicingStore>(opts =>
                    {
                        // All the normal options are available here
                        opts.Connection("different connection string");

                        // more configuration
                    })
                    // Optionally apply all database schema
                    // changes on startup
                    .ApplyAllDatabaseChangesOnStartup()

                    // Run the async daemon for this database
                    .AddAsyncDaemon(DaemonMode.HotCold)

                    // Use IInitialData
                    .InitializeWith(new DefaultDataSet())

                    // Use the V5 optimized artifact workflow
                    // with the separate store as well
                    .OptimizeArtifactWorkflow();
            }).StartAsync();

        #endregion
    }
}

public class DefaultDataSet: IInitialData
{
    public Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        throw new System.NotImplementedException();
    }
}

#region sample_IInvoicingStore

// These marker interfaces *must* be public
public interface IInvoicingStore : IDocumentStore
{

}

#endregion

#region sample_InvoicingService

public class InvoicingService
{
    private readonly IInvoicingStore _store;

    // IInvoicingStore can be injected like any other
    // service in your IoC container
    public InvoicingService(IInvoicingStore store)
    {
        _store = store;
    }

    public async Task DoSomethingWithInvoices()
    {
        // Important to dispose the session when you're done
        // with it
        await using var session = _store.LightweightSession();

        // do stuff with the session you just opened
    }
}

#endregion