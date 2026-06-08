using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Shouldly;
using Xunit;

namespace CoreTests.Events.Daemon;

// #4681 — the per-tenant high-water progression-row grammar is asymmetric to the rest of
// JasperFx.Events' ShardName (which collapses HighWaterMark identities to the constant).
// HighWaterShardIdentity is the canonical producer for the Marten-side per-tenant naming
// convention; pinning the exact shape here so any future grammar change has to revisit this
// test (and therefore the HighWaterDetector / HighWaterStatisticsDetector / BulkEventAppender
// SQL keyed by the same convention).
public class HighWaterShardIdentity_round_trip
{
    [Fact]
    public void store_global_equals_shardstate_constant()
    {
        HighWaterShardIdentity.StoreGlobal.ShouldBe(ShardState.HighWaterMark);
    }

    [Fact]
    public void per_tenant_prefix_is_constant_followed_by_colon()
    {
        HighWaterShardIdentity.PerTenantPrefix.ShouldBe(ShardState.HighWaterMark + ":");
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("tenant-with-dashes")]
    [InlineData("Mixed_Case-99")]
    public void per_tenant_concatenates_prefix_and_tenant(string tenantId)
    {
        HighWaterShardIdentity.PerTenant(tenantId).ShouldBe(
            $"{HighWaterShardIdentity.PerTenantPrefix}{tenantId}");
    }

    [Fact]
    public void per_tenant_prefix_matches_what_HighWaterDetector_uses_in_sql()
    {
        // HighWaterDetector.loadPerTenantStatistics passes :prefix as a parameter and joins
        // `prog.name = :prefix || i.tenant_id`. PerTenant must produce the same string the
        // join evaluates to in PostgreSQL, end to end.
        HighWaterShardIdentity.PerTenant("acme")
            .ShouldBe(HighWaterShardIdentity.PerTenantPrefix + "acme");
    }
}
