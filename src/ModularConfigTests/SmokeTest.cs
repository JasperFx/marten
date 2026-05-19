using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularConfigTests.SatelliteA;
using ModularConfigTests.SatelliteB;
using Shouldly;

namespace ModularConfigTests;

/// <summary>
/// Marten#4472 headline smoke test. Composes a real modular host: the main
/// assembly registers each satellite's IConfigureMarten /
/// IAsyncConfigureMarten via DI, AddMarten() runs with no inline projection
/// wiring, and the satellites' contributions land on the StoreOptions
/// before the DocumentStore is built. The test then exercises both
/// satellites' projections end-to-end against the CI Postgres database.
/// </summary>
public class SmokeTest
{
    [Fact]
    public async Task modular_configuration_with_satellite_assemblies_works_end_to_end()
    {
        var schemaName = ConfigurationFixture.UniqueSchemaName("modular_smoke");

        var builder = Host.CreateApplicationBuilder();

        // Force satellite assemblies to load via type reference. ProjectReference
        // puts the .dll in the bin dir, but AppDomain.CurrentDomain.GetAssemblies()
        // only returns LOADED assemblies — the typeof() references below + the
        // IConfigureMarten / IAsyncConfigureMarten singletons force the load so
        // Marten's DiscoverGeneratedEvolvers can see them.
        builder.Services.AddSingleton<IConfigureMarten, OrdersConfig>();

        // IAsyncConfigureMarten implementations only run if the
        // AsyncConfigureMartenApplication hosted service is also registered.
        // services.ConfigureMartenWithServices<T>() wires both; a bare
        // AddSingleton<IAsyncConfigureMarten>() would add the implementation
        // to DI but never invoke it (the hosted service is the only entry
        // point that calls IAsyncConfigureMarten.Configure). See #4493.
        builder.Services.ConfigureMartenWithServices<ReportingConfig>();

        ConfigurationFixture.AddBaselineMarten(builder.Services, schemaName);

        using var host = builder.Build();
        await host.StartAsync();

        try
        {
            // ASSERTION 1: Both satellites' projections are registered. If
            // DiscoverGeneratedEvolvers didn't find them, the post-#276
            // fail-fast (JasperFxAggregationProjectionBase.AssembleAndAssertValidity)
            // would have thrown InvalidProjectionException during AddMarten /
            // host.Build() — implicit pass when we reach this point.
            // Cast to the concrete DocumentStore to reach the writeable
            // StoreOptions.Projections (the IDocumentStore-facing
            // IReadOnlyStoreOptions doesn't expose the projection graph).
            var store = (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
            var registered = store.Options.Projections.All.Select(p => p.GetType()).ToList();
            registered.ShouldContain(typeof(OrderProjection));
            registered.ShouldContain(typeof(DailyProjection));

            // ASSERTION 2: End-to-end dispatch through SatelliteA's
            // SingleStreamProjection. StartStream + Append two events,
            // SaveChanges runs the inline OrderProjection over them, and
            // LoadAsync resolves the stored aggregate.
            var orderId = Guid.NewGuid();
            await using (var session = store.LightweightSession())
            {
                session.Events.StartStream<Order>(orderId, new OrderPlaced(orderId, 100m));
                session.Events.Append(orderId, new OrderShipped(orderId));
                await session.SaveChangesAsync();
            }

            await using (var query = store.QuerySession())
            {
                var order = await query.LoadAsync<Order>(orderId);
                order.ShouldNotBeNull();
                order!.Amount.ShouldBe(100m);
                order.IsShipped.ShouldBeTrue();
            }

            // ASSERTION 3: End-to-end dispatch through SatelliteB's
            // MultiStreamProjection. Two source streams contribute Daily
            // events keyed on the same Day, and the projection rolls them
            // up into one Daily aggregate.
            var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var streamA = Guid.NewGuid();
            var streamB = Guid.NewGuid();

            await using (var session = store.LightweightSession())
            {
                session.Events.StartStream(streamA, new DailyOpened(day));
                session.Events.StartStream(streamB, new DailyOpened(day));
                session.Events.Append(streamA, new DailyClosed(day));
                await session.SaveChangesAsync();
            }

            await using (var query = store.QuerySession())
            {
                var daily = await query.LoadAsync<Daily>(day);
                daily.ShouldNotBeNull();
                daily!.OpenCount.ShouldBe(2);
                daily.CloseCount.ShouldBe(1);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
