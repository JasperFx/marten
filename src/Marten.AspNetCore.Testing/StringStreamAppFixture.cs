using System;
using System.Threading.Tasks;
using Alba;
using IssueService;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marten.AspNetCore.Testing;

public class StringStreamAppFixture: IAsyncLifetime
{
    private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        Host = await AlbaHost.For<Program>(b =>
        {
            b.ConfigureServices((context, services) =>
            {
                services.MartenDaemonModeIsSolo();

                services.Configure<MartenSettings>(s =>
                {
                    s.SchemaName = SchemaName;
                    s.UseStringStreamIdentity = true;
                });
            });
        });
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
    }
}

[CollectionDefinition("string_stream_integration")]
public class StringStreamIntegrationCollection: ICollectionFixture<StringStreamAppFixture>
{
}
