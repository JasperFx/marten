using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// The bulk event import used to draw seq_ids from the STORE-GLOBAL <c>mt_events_sequence</c>. On a
/// per-tenant-partitioned store live appends draw from the tenant's own
/// <c>mt_events_sequence_{suffix}</c> instead (see QuickAppendEventFunction), so an import that bypasses
/// it leaves that sequence at 1 while the tenant's events already occupy seq_ids 1..N: the tenant's
/// FIRST live append after the import re-issues seq_id 1 — a primary-key collision on the tenant's
/// events partition — and even a surviving append would land below the tenant's high-water mark and be
/// silently skipped by the daemon. Caught by a production-copy spot check (ZOD-1714 canary): every
/// migrated tenant had last_value = 1 with tens of thousands of imported events.
///
/// The import must draw from the tenant's own sequence, so its position ends exactly at the tenant's
/// max seq_id and live appends continue seamlessly.
/// </summary>
[Collection("guid-partitioned")]
public class bulk_insert_draws_from_the_tenants_own_sequence
{
    private readonly GuidPartitionedFixture _fixture;

    public bulk_insert_draws_from_the_tenants_own_sequence(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task the_tenants_sequence_ends_at_max_seq_id_and_a_live_append_continues_after_it()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        var action = StreamAction.Start(_fixture.Store.Events, streamId,
            new TripStarted(streamId), new TripLeg(1.0), new TripLeg(2.0));
        action.TenantId = tenant;

        await _fixture.Store.BulkInsertEventsAsync(tenant, new[] { action });

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        long maxSeq;
        await using (var cmd = new NpgsqlCommand(
                         $"select max(seq_id) from {_fixture.SchemaName}.mt_events where tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            maxSeq = (long)(await cmd.ExecuteScalarAsync())!;
        }

        maxSeq.ShouldBe(3);

        // The tenant's OWN sequence supplied those seq_ids, so its position is the tenant's height.
        await using (var cmd = new NpgsqlCommand(
                         $"select last_value from {_fixture.SchemaName}.mt_events_sequence_{tenant}", conn))
        {
            ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(maxSeq,
                "the bulk import must draw seq_ids from the tenant's own sequence — leaving it behind " +
                "makes the tenant's first live append re-issue an already-used seq_id");
        }

        // The decisive behavior: a LIVE append directly after the import must succeed and continue
        // numbering after the imported events (no PK collision, not below the high-water mark).
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamId, new TripLeg(3.0));
            await session.SaveChangesAsync();
        }

        await using (var cmd = new NpgsqlCommand(
                         $"select max(seq_id) from {_fixture.SchemaName}.mt_events where tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(maxSeq + 1);
        }
    }
}
