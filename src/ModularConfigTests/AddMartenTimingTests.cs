using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace ModularConfigTests;

/// <summary>
/// Pin test for the AddMarten-timing contract: <see cref="IConfigureMarten"/>
/// registered AFTER <c>services.AddMarten(...)</c> in the DI sequence still
/// applies to the built <see cref="DocumentStore"/>. The contract is
/// order-independent for the AddMarten-vs-IConfigureMarten registration
/// pair — `IEnumerable&lt;IConfigureMarten&gt;` is resolved at store-build
/// time from the final DI snapshot.
/// </summary>
public class AddMartenTimingTests
{
    [Fact]
    public async Task configure_marten_registered_after_AddMarten_still_applies()
    {
        var schemaName = ConfigurationFixture.UniqueSchemaName("modular_timing");

        var builder = Host.CreateApplicationBuilder();

        // AddMarten FIRST (so the StoreOptions factory is in DI before
        // the IConfigureMarten singleton).
        ConfigurationFixture.AddBaselineMarten(builder.Services, schemaName);

        // Then register the IConfigureMarten AFTER AddMarten.
        builder.Services.AddSingleton<IConfigureMarten>(new SetNameLength(NameDataLengthSentinel));

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
            store.Options.NameDataLength.ShouldBe(NameDataLengthSentinel);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private const int NameDataLengthSentinel = 250;

    private sealed class SetNameLength : IConfigureMarten
    {
        private readonly int _value;
        public SetNameLength(int value) => _value = value;
        public void Configure(System.IServiceProvider services, StoreOptions options)
            => options.NameDataLength = _value;
    }
}
