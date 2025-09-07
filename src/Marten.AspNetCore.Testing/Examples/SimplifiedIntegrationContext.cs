using System.Threading.Tasks;
using Alba;
using Xunit;

namespace Marten.AspNetCore.Testing.Examples;

#region sample_simplified_integration_context
public abstract class SimplifiedIntegrationContext : IAsyncLifetime
{
    protected SimplifiedIntegrationContext(AppFixture fixture)
    {
        Host = fixture.Host;
        Store = Host.DocumentStore();
    }

    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }

    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state
        await Store.Advanced.ResetAllData();

        // OR if you use the async daemon in your tests, use this
        // instead to do the above, but also cleanly stop all projections,
        // reset the data, then start all async projections and subscriptions up again
        await Host.ResetAllMartenDataAsync();
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
