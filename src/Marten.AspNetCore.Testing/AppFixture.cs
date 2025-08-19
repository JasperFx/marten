using System;
using System.Threading.Tasks;
using Alba;
using IssueService;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marten.AspNetCore.Testing;

#region sample_integration_appfixture
public class AppFixture: IAsyncLifetime
{
#region sample_integration_scheme_name
    private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
#endregion
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        // This is bootstrapping the actual application using
        // its implied Program.Main() set up
#region sample_integration_configure_scheme_name
        Host = await AlbaHost.For<Program>(b =>
        {
            b.ConfigureServices((context, services) =>
            {
                // Important! You can make your test harness work a little faster (important on its own)
                // and probably be more reliable by overriding your Marten configuration to run all
                // async daemons in "Solo" mode so they spin up faster and there's no issues from
                // PostgreSQL having trouble with advisory locks when projections are rapidly started and stopped

                // This was added in V8.8
                services.MartenDaemonModeIsSolo();

                services.Configure<MartenSettings>(s =>
                {
                    s.SchemaName = SchemaName;
                });
            });
        });
#endregion
    }

    public async Task DisposeAsync()
        {
            await Host.DisposeAsync();
        }
    }
#endregion
