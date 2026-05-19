using System.Threading.Tasks;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace ModularConfigTests;

/// <summary>
/// Pin test for the last-wins conflict-resolution contract: two
/// <see cref="IConfigureMarten"/> implementations register the same event
/// type (idempotent registration) and overlap on the same scalar
/// <see cref="StoreOptions"/> property. The host must not throw on the
/// event-type re-registration, and the scalar property must reflect the
/// later config's value (LIFO over the IEnumerable&lt;IConfigureMarten&gt;
/// resolved from DI).
///
/// This pin guards against a future change that would throw on idempotent
/// duplicate event-type registration — composite-config patterns rely on
/// the idempotency to let two satellites independently declare the same
/// shared event without coordinating.
/// </summary>
public class LastWinsTests
{
    [Fact]
    public async Task duplicate_event_type_registration_is_idempotent_and_last_scalar_wins()
    {
        var schemaName = ConfigurationFixture.UniqueSchemaName("modular_lastwins");

        var builder = Host.CreateApplicationBuilder();

        // Each contribution registers SatelliteA's OrderPlaced event AND sets
        // NameDataLength to its own sentinel value. The first registration's
        // event registration is idempotent w.r.t. the second; the scalar
        // setter is overwritten.
        builder.Services.AddSingleton<IConfigureMarten>(new DuplicateContribution(nameLength: 100));
        builder.Services.AddSingleton<IConfigureMarten>(new DuplicateContribution(nameLength: 250));

        ConfigurationFixture.AddBaselineMarten(builder.Services, schemaName);

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
            // No DuplicateSubscriptionNamesException / NotSupportedException
            // on host build: event-type re-registration is idempotent.
            // Last contribution's scalar value wins.
            // The host built without throwing — that's the dispositive
            // evidence for the idempotent-event-registration claim
            // (otherwise AddEventType would have thrown on the second
            // contribution).
            store.ShouldNotBeNull();
            // Last contribution's scalar value wins (LIFO over the DI
            // resolution of IEnumerable<IConfigureMarten>).
            store.Options.NameDataLength.ShouldBe(250);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private sealed class DuplicateContribution : IConfigureMarten
    {
        private readonly int _nameLength;
        public DuplicateContribution(int nameLength) => _nameLength = nameLength;

        public void Configure(System.IServiceProvider services, StoreOptions options)
        {
            options.Events.AddEventType(typeof(SatelliteA.OrderPlaced));
            options.NameDataLength = _nameLength;
        }
    }
}
