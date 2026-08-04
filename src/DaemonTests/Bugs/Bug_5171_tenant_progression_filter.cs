using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5171 — the per-tenant progression filter used an unescaped <c>LIKE '%:' || tenantId</c>. Since
/// <c>_</c> is both a legal tenant-id character and a LIKE single-character wildcard, a read scoped to
/// <c>acme_corp</c> also matched <c>acmeXcorp</c>'s rows and reported them as <c>acme_corp</c>'s progress.
/// The narrower sibling: a tenant-less identity whose shard key equals the tenant id (<c>Foo:acme</c>)
/// ends in the same suffix and was attributed to the tenant as well.
/// </summary>
public class Bug_5171_tenant_progression_filter: OneOffConfigurationsContext, IAsyncLifetime
{
    public override async ValueTask InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return base.DisposeAsync();
    }

    private async Task seedProgression(params (ShardName name, long sequence)[] rows)
    {
        foreach (var (name, sequence) in rows)
        {
            theSession.QueueOperation(new InsertProjectionProgress(theStore.Events, new EventRange(name, sequence)));
        }

        await theSession.SaveChangesAsync();
    }

    private Task<System.Collections.Generic.IReadOnlyList<ShardState>> progressFor(string tenantId) =>
        ((IEventDatabase)theStore.Tenancy.Default.Database).AllProjectionProgress(tenantId, CancellationToken.None);

    [Fact]
    public async Task underscore_in_a_tenant_id_is_not_treated_as_a_wildcard()
    {
        await seedProgression(
            (ShardName.Compose("Orders", tenantId: "acme_corp"), 10),
            (ShardName.Compose("Orders", tenantId: "acmeXcorp"), 99),
            (ShardName.Compose("Orders", tenantId: "acme-corp"), 77));

        var rows = await progressFor("acme_corp");

        rows.Select(x => x.ShardName).ShouldBe(["Orders:All:acme_corp"]);
        rows.Single().Sequence.ShouldBe(10);

        // ...and the neighbors still resolve to their own rows, at their own heights.
        (await progressFor("acmeXcorp")).Single().Sequence.ShouldBe(99);
        (await progressFor("acme-corp")).Single().Sequence.ShouldBe(77);
    }

    [Fact]
    public async Task percent_in_a_tenant_id_does_not_match_everything()
    {
        await seedProgression(
            (ShardName.Compose("Orders", tenantId: "a%b"), 10),
            (ShardName.Compose("Orders", tenantId: "axxb"), 99));

        (await progressFor("a%b")).Single().Sequence.ShouldBe(10);
    }

    [Fact]
    public async Task a_shard_key_equal_to_a_tenant_id_is_not_reported_as_that_tenants_progress()
    {
        await seedProgression(
            (ShardName.Compose("Orders", shardKey: "acme"), 500),                 // Orders:acme    — no tenant
            (ShardName.Compose("Orders", shardKey: "acme", version: 2), 600),     // Orders:V2:acme — no tenant
            (ShardName.Compose("Orders", tenantId: "acme"), 7));                  // Orders:All:acme

        var rows = await progressFor("acme");

        rows.Select(x => x.ShardName).ShouldBe(["Orders:All:acme"]);
        rows.Single().Sequence.ShouldBe(7);
    }

    [Fact]
    public async Task the_tenants_high_water_row_is_still_included()
    {
        await seedProgression(
            (ShardName.HighWaterMarkFor("acme_corp"), 250),
            (ShardName.Compose("Orders", tenantId: "acme_corp"), 10));

        var rows = await progressFor("acme_corp");

        rows.Select(x => x.ShardName).OrderBy(x => x, System.StringComparer.Ordinal)
            .ShouldBe(["HighWaterMark:acme_corp", "Orders:All:acme_corp"]);
    }

    [Fact]
    public async Task a_null_tenant_still_returns_every_row()
    {
        await seedProgression(
            (ShardName.Compose("Orders"), 1),
            (ShardName.Compose("Orders", shardKey: "acme"), 2),
            (ShardName.Compose("Orders", tenantId: "acme"), 3));

        var rows = await ((IEventDatabase)theStore.Tenancy.Default.Database)
            .AllProjectionProgress(tenantId: null, CancellationToken.None);

        rows.Select(x => x.ShardName).OrderBy(x => x, System.StringComparer.Ordinal)
            .ShouldBe(["Orders:All", "Orders:All:acme", "Orders:acme"]);
    }
}
