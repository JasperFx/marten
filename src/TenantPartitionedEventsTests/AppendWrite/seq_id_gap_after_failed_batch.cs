using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// #4617 section 3a — pin the gap-tolerance shape on the per-tenant sequence.
/// PostgreSQL sequences are non-transactional: <c>nextval</c> advances the
/// sequence even if the surrounding transaction rolls back. So a failed
/// append (e.g. MT001 archived-stream rejection) consumes seq_id values that
/// never reach <c>mt_events</c> — the next successful append's seq_id is
/// strictly greater than the last successful one, but there's a HOLE in
/// between.
///
/// <para>
/// Consumers reading from <c>mt_events</c> ordered by <c>seq_id</c> must be
/// gap-tolerant. The daemon's HighWaterMark / GapDetector already handles
/// this. Pinned here so the gap shape is part of the documented contract.
/// </para>
/// </summary>
[Collection("guid-partitioned")]
public class seq_id_gap_after_failed_batch
{
    private readonly GuidPartitionedFixture _fixture;

    public seq_id_gap_after_failed_batch(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task per_tenant_seq_id_is_monotonic_with_gaps_pin()
    {
        // PostgreSQL sequences are non-transactional — `nextval` advances the
        // sequence even if the surrounding transaction rolls back. This test
        // pins the consumer-facing invariant: the per-tenant sequence is
        // strictly increasing but NOT contiguous; consumers reading mt_events
        // ordered by seq_id must tolerate gaps.
        //
        // Cause-agnostic simulation: explicitly burn one nextval() to model the
        // shape a partial-function-execution or pooled-batch-failure would
        // leave behind. (The archived-stream MT001 path doesn't actually
        // produce a gap — the function raises BEFORE the per-event nextval loop
        // — so a synthetic burn is the most direct way to pin the invariant
        // without coupling to a specific failure mode.)
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // 1) Append one event to make the tenant's sequence active + reachable.
        var firstStream = Guid.NewGuid();
        long lastSeqBeforeBurn;
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(firstStream, new TripStarted(firstStream));
            await s.SaveChangesAsync();
            var seeded = await s.Events.QueryAllRawEvents()
                .Where(e => e.StreamId == firstStream)
                .ToListAsync();
            lastSeqBeforeBurn = seeded.Max(e => e.Sequence);
        }

        // 2) Burn ONE nextval directly on the per-tenant sequence — this
        //    models the gap a partial / failed batch would produce.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand(
                $"select nextval('{_fixture.SchemaName}.mt_events_sequence_{tenant}')");
            await cmd.ExecuteScalarAsync();
        }

        // 3) Successful append — its first seq_id must SKIP the burned value.
        var nextStream = Guid.NewGuid();
        long nextStreamFirstSeq;
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(nextStream, new TripStarted(nextStream));
            await s.SaveChangesAsync();
            var events = await s.Events.QueryAllRawEvents()
                .Where(e => e.StreamId == nextStream)
                .ToListAsync();
            nextStreamFirstSeq = events.Single().Sequence;
        }

        // The pin: the next successful seq_id is GREATER than
        // lastSeqBeforeBurn + 1 — there's a hole that consumers must tolerate.
        nextStreamFirstSeq.ShouldBeGreaterThan(lastSeqBeforeBurn + 1,
            "the burned nextval consumed at least one seq_id value that never landed in " +
            "mt_events — pin the resulting gap so consumers are reminded the per-tenant " +
            "sequence is monotonic-with-gaps, not strictly contiguous");
    }
}
