using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// BulkInsertEventsAsync (via BulkEventAppender) used to advance ONLY the store-global 'HighWaterMark'
/// progression row. On a per-tenant-partitioned store that row is never read — the async daemon's
/// per-tenant coordinators read "HighWaterMark:&lt;tenant&gt;" — so the bulk-loaded events were invisible
/// to the high-water machinery until the next full detection poll recomputed them, and the store-global
/// row was left holding a misleading max-across-all-tenants value.
///
/// After the fix, on a per-tenant-partitioned store the bulk appender advances each affected tenant's own
/// "HighWaterMark:&lt;tenant&gt;" row (to that tenant's max seq_id), so the per-tenant projection catch-up
/// can start immediately and nothing writes the meaningless store-global row.
/// </summary>
[Collection("guid-partitioned")]
public class bulk_insert_advances_per_tenant_high_water
{
    private readonly GuidPartitionedFixture _fixture;

    public bulk_insert_advances_per_tenant_high_water(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task advances_the_per_tenant_row_to_the_tenants_max_seq_id()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        var action = StreamAction.Start(_fixture.Store.Events, streamId,
            new TripStarted(streamId), new TripLeg(1.0), new TripLeg(2.0), new TripLeg(3.0), new TripLeg(4.0));
        action.TenantId = tenant;

        await _fixture.Store.BulkInsertEventsAsync(tenant, new[] { action });

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        // The tenant's true height = max(seq_id) among its bulk-loaded events.
        long maxSeq;
        await using (var cmd = new NpgsqlCommand(
                         $"select max(seq_id) from {_fixture.SchemaName}.mt_events where tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            maxSeq = (long)(await cmd.ExecuteScalarAsync())!;
        }

        maxSeq.ShouldBeGreaterThan(0);

        // The per-tenant high-water row must be advanced to exactly that height.
        await using (var cmd = new NpgsqlCommand(
                         $"select last_seq_id from {_fixture.SchemaName}.mt_event_progression where name = @name", conn))
        {
            cmd.Parameters.AddWithValue("name", HighWaterShardIdentity.PerTenant(tenant));
            var perTenant = await cmd.ExecuteScalarAsync();
            perTenant.ShouldNotBeNull(
                "bulk insert must write the per-tenant HighWaterMark:<tenant> row on a per-tenant-partitioned store");
            ((long)perTenant!).ShouldBe(maxSeq);
        }

        // And it must not have advanced the store-global row to this tenant's height (nothing reads it here,
        // and it would misrepresent a single tenant's max as the whole store's).
        await using (var cmd = new NpgsqlCommand(
                         $"select last_seq_id from {_fixture.SchemaName}.mt_event_progression where name = @name", conn))
        {
            cmd.Parameters.AddWithValue("name", HighWaterShardIdentity.StoreGlobal);
            var global = await cmd.ExecuteScalarAsync();
            if (global is not null)
            {
                ((long)global!).ShouldNotBe(maxSeq,
                    "bulk insert on a per-tenant-partitioned store must not advance the store-global HighWaterMark");
            }
        }
    }
}
