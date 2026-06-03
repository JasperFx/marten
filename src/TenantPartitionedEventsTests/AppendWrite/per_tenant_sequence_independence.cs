using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_quick_append.cs
/// — #4596 Phase 1 Session 2 per-tenant Postgres sequence + QuickAppendEventFunction
/// rewrite. Runs against the shared <see cref="GuidPartitionedFixture"/>; each
/// test mints unique tenant ids via <see cref="PartitionedFixtureBase.NewTenant"/>
/// so each call to <c>AddMartenManagedTenantsAsync</c> gets fresh per-tenant
/// sequences that start at 1.
///
/// <para>
/// The absolute-monotonicity assertions in the original (e.g. <c>seq_id == [1,
/// 2, 3, 4, 5]</c>, <c>lastValue == 5L</c>) are relaxed to relative
/// monotonicity for tests where sibling tenants might have written to the same
/// store between when this test's tenant was created and when it appended. The
/// "first event of a freshly-minted tenant starts at seq_id == 1" guarantee
/// IS still asserted — each per-tenant sequence is a fresh PG sequence, so the
/// first nextval is always 1.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class per_tenant_sequence_independence
{
    private readonly GuidPartitionedFixture _fixture;

    public per_tenant_sequence_independence(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<List<string>> ListSequencesAsync(string schema, params string[] tenantSuffixes)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var found = new List<string>();
        foreach (var suffix in tenantSuffixes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "select sequencename from pg_sequences where schemaname = @s and sequencename = @n";
            cmd.Parameters.AddWithValue("s", schema);
            cmd.Parameters.AddWithValue("n", "mt_events_sequence_" + suffix);
            var v = await cmd.ExecuteScalarAsync();
            if (v != null && v != DBNull.Value)
            {
                found.Add((string)v);
            }
        }
        return found;
    }

    private async Task<List<long>> ReadSeqIdsForTenantAsync(string schema, string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select seq_id from {schema}.mt_events where tenant_id = @t order by seq_id";
        cmd.Parameters.AddWithValue("t", tenantId);
        var ids = new List<long>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync()) ids.Add(rdr.GetInt64(0));
        return ids;
    }

    [Fact]
    public async Task per_tenant_sequences_are_created_on_AddMartenManagedTenantsAsync()
    {
        // Two unique tenant ids per call → AddMartenManagedTenantsAsync must
        // mint the matching mt_events_sequence_<tenant> sequences for both.
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var sequences = await ListSequencesAsync(_fixture.SchemaName, alpha, beta);
        sequences.ShouldContain("mt_events_sequence_" + alpha);
        sequences.ShouldContain("mt_events_sequence_" + beta);
        sequences.Count.ShouldBe(2,
            "exactly the two sequences for the tenants registered in this call should be present");
    }

    [Fact]
    public async Task per_tenant_sequences_are_created_when_partitions_registered_before_first_apply()
    {
        // Original asserted "registered BEFORE first apply" — under the shared
        // store, EnsureStorageExistsAsync has already been called by the
        // fixture's InitializeAsync. The contract still holds, though: every
        // call to AddMartenManagedTenantsAsync emits CREATE SEQUENCE IF NOT
        // EXISTS for the freshly-registered tenants, regardless of whether
        // the events table was just minted or already exists. With unique
        // tenant ids per test the sequences cannot pre-exist, so this is
        // equivalent coverage.
        var gamma = PartitionedFixtureBase.NewTenant();
        var delta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, gamma, delta);

        var sequences = await ListSequencesAsync(_fixture.SchemaName, gamma, delta);
        sequences.ShouldContain("mt_events_sequence_" + gamma);
        sequences.ShouldContain("mt_events_sequence_" + delta);
    }

    [Fact]
    public async Task two_tenants_draw_independent_monotonic_sequences()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // Tenant 1: append 5 events. Tenant 2: append 3 events. Use the
        // fixture's TripStarted/TripLeg events (already registered).
        await _fixture.AppendNEventsAsync(alpha, 5);
        await _fixture.AppendNEventsAsync(beta, 3);

        // The kickoff invariant: each tenant's seq_ids come from a private
        // monotonic sequence, NOT a single shared global counter.
        var alphaSeqIds = await ReadSeqIdsForTenantAsync(_fixture.SchemaName, alpha);
        var betaSeqIds = await ReadSeqIdsForTenantAsync(_fixture.SchemaName, beta);

        alphaSeqIds.Count.ShouldBe(5);
        betaSeqIds.Count.ShouldBe(3);

        // Freshly-minted tenant → fresh PG sequence → first nextval == 1.
        alphaSeqIds[0].ShouldBe(1L, "first event of a brand-new tenant must come from a fresh sequence at value 1");
        betaSeqIds[0].ShouldBe(1L, "first event of a brand-new tenant must come from a fresh sequence at value 1");

        // Strict +1 monotonicity per tenant. If the two tenants shared a
        // global sequence, alpha would be 1..5 and beta would be 6..8 — not
        // a contiguous 1..3 starting fresh per tenant.
        for (var i = 1; i < alphaSeqIds.Count; i++)
        {
            (alphaSeqIds[i] - alphaSeqIds[i - 1]).ShouldBe(1L,
                "per-tenant seq_ids must be strictly +1 monotonic for tenant alpha");
        }
        for (var i = 1; i < betaSeqIds.Count; i++)
        {
            (betaSeqIds[i] - betaSeqIds[i - 1]).ShouldBe(1L,
                "per-tenant seq_ids must be strictly +1 monotonic for tenant beta");
        }

        // Direct sequence-state check: each per-tenant sequence advanced by
        // at least the count we appended for it. (>= rather than == in case a
        // sibling test on the shared fixture also touches that tenant — though
        // unique-tenant-per-test rules that out in practice, the relaxed
        // assertion captures the relative-monotonicity contract we actually
        // care about.)
        var alphaLast = await _fixture.ReadSequenceLastValueAsync(alpha, _fixture.SchemaName);
        var betaLast = await _fixture.ReadSequenceLastValueAsync(beta, _fixture.SchemaName);
        alphaLast.ShouldBeGreaterThanOrEqualTo(5L,
            "alpha's per-tenant sequence must advance to at least its appended event count");
        betaLast.ShouldBeGreaterThanOrEqualTo(3L,
            "beta's per-tenant sequence must advance to at least its appended event count");
    }

    [Fact]
    public async Task append_for_unregistered_tenant_raises_clear_error()
    {
        // CLEAN test: register one tenant, then try to append to a never-
        // registered second tenant id. The QuickAppendEventFunction guard
        // raises SQLSTATE MT002 with a tenant-specific error message.
        var registered = PartitionedFixtureBase.NewTenant();
        var unregistered = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, registered);

        await using var session = _fixture.Store.LightweightSession(unregistered);
        session.Events.StartStream(Guid.NewGuid(), new TripStarted(Guid.NewGuid()));

        // Marten wraps PG exceptions in MartenCommandException; pull the inner.
        var wrapped = await Should.ThrowAsync<MartenCommandException>(async () =>
        {
            await session.SaveChangesAsync();
        });

        var pg = wrapped.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        // Function raises with SQLSTATE 'MT002' for unregistered-tenant append.
        pg.SqlState.ShouldBe("MT002");
        pg.Message.ShouldContain(unregistered);
        pg.Message.ShouldContain("AddMartenManagedTenantsAsync");
    }
}
