using System;
using System.Threading.Tasks;
using Alba;
using IssueService;
using Xunit;

namespace Marten.AspNetCore.Testing;

#region sample_integration_appfixture
public class AppFixture : IAsyncLifetime
{
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        Host = await Program.CreateHostBuilder(Array.Empty<string>())
            .StartAlbaAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}
#endregion
