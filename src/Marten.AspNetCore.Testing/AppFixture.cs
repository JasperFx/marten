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
