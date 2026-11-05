#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

public class rewriting_stored_events_per_tenant: PartitionedStoreContext
{
    public record ParcelSent(string To);

    protected override string SchemaPrefix => "tp_rw";

    protected override void ConfigureStore(StoreOptions opts) => opts.Events.AddEventType<ParcelSent>();

    [Fact]
    public async Task every_tenants_events_are_rewritten_although_they_share_sequence_numbers()
    {
        string[] tenants = ["alpha", "beta", "gamma"];
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        foreach (var tenant in tenants)
        {
            await using var session = Store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new ParcelSent($"{tenant} 1"), new ParcelSent($"{tenant} 2"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // One event per page, so every page boundary falls between two tenants' same-numbered events
        var result = await Store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.Transform<ParcelSent>(e => e with { To = e.To.ToUpperInvariant() });
            x.BatchSize = 1;
        }, TestContext.Current.CancellationToken);

        result.Databases.Single().Rewritten.ShouldBe(6);

        foreach (var tenant in tenants)
        {
            await using var query = Store.QuerySession(tenant);
            var events = await query.Events.QueryAllRawEvents().ToListAsync(TestContext.Current.CancellationToken);
            events.Select(e => ((ParcelSent)e.Data).To).OrderBy(x => x)
                .ShouldBe([$"{tenant.ToUpperInvariant()} 1", $"{tenant.ToUpperInvariant()} 2"]);
        }
    }
}
