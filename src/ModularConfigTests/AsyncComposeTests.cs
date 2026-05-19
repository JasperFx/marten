using System.Threading;
using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace ModularConfigTests;

/// <summary>
/// Pin test for the IConfigureMarten + IAsyncConfigureMarten compose
/// contract: both run when registered, both contributions land on the
/// final <see cref="StoreOptions"/>. The hosted service that drains
/// IAsyncConfigureMarten (<c>AsyncConfigureMartenApplication</c>) is
/// inserted ahead of MartenActivator in the IHostedService order so
/// async configs apply before the store is consumed.
/// </summary>
public class AsyncComposeTests
{
    [Fact]
    public async Task sync_and_async_configure_marten_both_run()
    {
        var schemaName = ConfigurationFixture.UniqueSchemaName("modular_async");

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<IConfigureMarten>(new SyncContribution());
        // Use the extension method (not raw AddSingleton<IAsyncConfigureMarten>)
        // so AsyncConfigureMartenApplication gets registered too. See #4493.
        builder.Services.ConfigureMartenWithServices<AsyncContribution>();

        ConfigurationFixture.AddBaselineMarten(builder.Services, schemaName);

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
            // Sync contribution sets NameDataLength to 100;
            // async contribution flips DisableNpgsqlLogging to true.
            store.Options.NameDataLength.ShouldBe(100);
            store.Options.DisableNpgsqlLogging.ShouldBeTrue();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private sealed class SyncContribution : IConfigureMarten
    {
        public void Configure(System.IServiceProvider services, StoreOptions options)
            => options.NameDataLength = 100;
    }

    private sealed class AsyncContribution : IAsyncConfigureMarten
    {
        public ValueTask Configure(StoreOptions options, CancellationToken cancellationToken)
        {
            options.DisableNpgsqlLogging = true;
            return ValueTask.CompletedTask;
        }
    }
}
