using System.Threading.Tasks;
using Alba;
using Xunit;

namespace Marten.AspNetCore.Testing.Examples;

#region sample_multiple_databases_integration_context

public interface IInvoicingStore: IDocumentStore
{
}

public abstract class MultipleMartenDatabasesIntegrationContext: IAsyncLifetime
{
    protected MultipleMartenDatabasesIntegrationContext(
        AppFixture fixture
    )
    {
        Host = fixture.Host;
        Store = Host.DocumentStore();
        InvoicingStore = Host.DocumentStore<IInvoicingStore>();
    }

    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }
    public IInvoicingStore InvoicingStore { get; }

    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state
        await Store.Advanced.ResetAllData();
    }

    // This is required because of the IAsyncLifetime
    // interface. Note that I do *not* tear down database
    // state after the test. That's purposeful
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}

#endregion
