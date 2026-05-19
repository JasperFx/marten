using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace ModularConfigTests;

/// <summary>
/// Pin test for the modular-config registration-order contract: when two
/// <see cref="IConfigureMarten"/> implementations both write to the same
/// <see cref="StoreOptions"/> property, the LATER-registered one wins —
/// invocations happen in DI registration order, so the second call
/// overwrites the first.
/// </summary>
public class OrderingTests
{
    [Fact]
    public async Task last_registered_configure_marten_wins_when_setting_same_property()
    {
        var schemaName = ConfigurationFixture.UniqueSchemaName("modular_order");

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddSingleton<IConfigureMarten>(new SetUserName("first"));
        builder.Services.AddSingleton<IConfigureMarten>(new SetUserName("second"));
        builder.Services.AddSingleton<IConfigureMarten>(new SetUserName("third"));

        ConfigurationFixture.AddBaselineMarten(builder.Services, schemaName);

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
            store.Options.NameDataLength.ShouldBe(SetUserName.GetValue("third"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Sets <see cref="StoreOptions.NameDataLength"/> to a per-instance
    /// deterministic value so the test can read the final value and
    /// identify which IConfigureMarten ran last.
    /// </summary>
    private sealed class SetUserName : IConfigureMarten
    {
        private readonly string _label;
        public SetUserName(string label) => _label = label;

        public void Configure(System.IServiceProvider services, StoreOptions options)
            => options.NameDataLength = GetValue(_label);

        public static int GetValue(string label) => label switch
        {
            "first" => 100,
            "second" => 200,
            "third" => 300,
            _ => 0
        };
    }
}
