using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// marten#4879 — the migration flavor of the streaming bulk import. Under
/// <see cref="BulkEventSequenceMode.PreserveSourceSequence"/> on a per-tenant-partitioned store the
/// import must (a) keep the exact (gappy!) seq_ids the events carry — a conjoined source interleaved
/// every tenant on one global sequence, so a single tenant's seq_ids are full of holes and MUST NOT be
/// renumbered (marten#4682's data policy), (b) advance the tenant's own
/// <c>mt_events_sequence_{suffix}</c> past the imported maximum via setval so the first live append never
/// re-issues an imported seq_id, and (c) seed the tenant's <c>HighWaterMark:{tenant}</c> progression row
/// at the imported maximum so high-water detection starts ABOVE the gappy history instead of gap-walking
/// through it.
/// </summary>
[Collection("guid-partitioned")]
public class preserve_sequence_streaming_bulk_import
{
    private readonly GuidPartitionedFixture _fixture;

    public preserve_sequence_streaming_bulk_import(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    // Build a raw IEvent carrying its stream id, per-stream version, AND source seq_id — exactly what a
    // migration read of a conjoined source produces.
    private static IEvent EventFor(Guid streamId, long version, long sourceSequence, object data)
    {
        var e = (IEvent)Activator.CreateInstance(typeof(Event<>).MakeGenericType(data.GetType()), data)!;
        e.StreamId = streamId;
        e.Version = version;
        e.Id = Guid.NewGuid();
        e.Sequence = sourceSequence;
        return e;
    }

    private static async IAsyncEnumerable<IEvent> Stream(IEnumerable<IEvent> events)
    {
        foreach (var e in events)
        {
            yield return e;
        }

        await Task.CompletedTask;
    }

    private async Task<List<long>> SeqIdsForTenantAsync(string tenant)
    {
        var seqs = new List<long>();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select seq_id from {_fixture.SchemaName}.mt_events where tenant_id = @t order by seq_id", conn);
        cmd.Parameters.AddWithValue("t", tenant);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            seqs.Add(reader.GetInt64(0));
        }

        return seqs;
    }

    [Fact]
    public async Task preserves_gappy_source_seq_ids_advances_the_tenant_sequence_and_seeds_high_water()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamX = Guid.NewGuid();
        var streamY = Guid.NewGuid();

        var headers = new[]
        {
            new BulkEventStreamHeader { Id = streamX, Version = 3 },
            new BulkEventStreamHeader { Id = streamY, Version = 2 }
        };

        // Gappy source seq_ids, as a conjoined source's per-tenant slice really looks: other tenants own
        // the holes. Interleaved X/Y to also pin cross-stream order preservation.
        var events = new[]
        {
            EventFor(streamX, 1, 3, new TripStarted(streamX)),
            EventFor(streamY, 1, 7, new TripStarted(streamY)),
            EventFor(streamX, 2, 12, new TripLeg(1.0)),
            EventFor(streamY, 2, 40, new TripLeg(2.0)),
            EventFor(streamX, 3, 41, new TripLeg(3.0))
        };

        await _fixture.Store.BulkInsertEventStreamAsync(tenant, headers, Stream(events),
            BulkEventSequenceMode.PreserveSourceSequence, batchSize: 2);

        // (a) The seq_ids are EXACTLY the source values — never renumbered, gaps intact.
        (await SeqIdsForTenantAsync(tenant)).ShouldBe(new long[] { 3, 7, 12, 40, 41 });

        // (b) The tenant's own sequence was advanced past the imported maximum...
        (await _fixture.ReadSequenceLastValueAsync(tenant, _fixture.SchemaName))
            .ShouldBeGreaterThanOrEqualTo(41);

        // ...so a live append lands ABOVE the imported history on the first try.
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamX, new TripLeg(4.0));
            await session.SaveChangesAsync();
        }

        var afterAppend = await SeqIdsForTenantAsync(tenant);
        afterAppend.Count.ShouldBe(6);
        afterAppend[^1].ShouldBeGreaterThan(41);

        // (c) The tenant's high-water progression row is seeded at the imported maximum.
        var rows = await _fixture.ReadProgressionRowsAsync(_fixture.SchemaName, $"HighWaterMark:{tenant}");
        rows.Count.ShouldBe(1);
        rows[0].LastSeqId.ShouldBe(41);
    }

    [Fact]
    public async Task rejects_events_that_are_not_strictly_ascending_and_commits_nothing()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var stream = Guid.NewGuid();
        var headers = new[] { new BulkEventStreamHeader { Id = stream, Version = 2 } };

        // 9 after 9 — a caller that forgot to order by the source seq_id (or fed unnumbered events, which
        // arrive as 0) must be rejected rather than silently corrupting the tenant's replay order.
        var events = new[]
        {
            EventFor(stream, 1, 9, new TripStarted(stream)),
            EventFor(stream, 2, 9, new TripLeg(1.0))
        };

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            _fixture.Store.BulkInsertEventStreamAsync(tenant, headers, Stream(events),
                BulkEventSequenceMode.PreserveSourceSequence));
        ex.Message.ShouldContain("strictly ascending");

        // The per-tenant transaction rolled back: nothing committed, clean retry possible.
        (await _fixture.CountEventsForTenantAsync(tenant, _fixture.SchemaName)).ShouldBe(0);
    }
}
