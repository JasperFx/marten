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
/// Partitioned-store coverage for the streaming <c>BulkInsertEventStreamAsync</c> import: on a
/// per-tenant-partitioned store the import must (a) preserve the supplied cross-stream order in the
/// assigned seq_ids, (b) draw those seq_ids from the tenant's OWN <c>mt_events_sequence_{suffix}</c> so
/// the sequence ends at the tenant's height, and (c) leave the tenant in a state where a LIVE append
/// directly after the import continues seamlessly (no PK collision, not below the tenant's high water).
/// </summary>
[Collection("guid-partitioned")]
public class streaming_bulk_import_on_a_partitioned_store
{
    private readonly GuidPartitionedFixture _fixture;

    public streaming_bulk_import_on_a_partitioned_store(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    // Build a raw IEvent carrying its stream id + per-stream version, as a migration read would produce.
    private static IEvent EventFor(Guid streamId, long version, object data)
    {
        var e = (IEvent)Activator.CreateInstance(typeof(Event<>).MakeGenericType(data.GetType()), data)!;
        e.StreamId = streamId;
        e.Version = version;
        e.Id = Guid.NewGuid();
        e.Sequence = version; // source seq is irrelevant: the streaming path assigns by arrival order
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

    [Fact]
    public async Task preserves_cross_stream_order_ends_the_tenant_sequence_at_max_and_supports_live_appends()
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

        // Interleaved source order: X1, Y1, X2, Y2, X3 — the assigned seq_ids must follow it.
        var events = new[]
        {
            EventFor(streamX, 1, new TripStarted(streamX)),
            EventFor(streamY, 1, new TripStarted(streamY)),
            EventFor(streamX, 2, new TripLeg(1.0)),
            EventFor(streamY, 2, new TripLeg(2.0)),
            EventFor(streamX, 3, new TripLeg(3.0))
        };

        await _fixture.Store.BulkInsertEventStreamAsync(tenant, headers, Stream(events), batchSize: 2);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        // (a) Cross-stream order preserved: reading back by seq_id yields the interleaving.
        var order = new List<Guid>();
        await using (var cmd = new NpgsqlCommand(
                         $"select stream_id from {_fixture.SchemaName}.mt_events where tenant_id = @t order by seq_id",
                         conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                order.Add(reader.GetGuid(0));
            }
        }

        order.ShouldBe(new[] { streamX, streamY, streamX, streamY, streamX });

        // (b) The tenant's own sequence supplied the seq_ids, so its position is the tenant's height.
        long maxSeq;
        await using (var cmd = new NpgsqlCommand(
                         $"select max(seq_id) from {_fixture.SchemaName}.mt_events where tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            maxSeq = (long)(await cmd.ExecuteScalarAsync())!;
        }

        maxSeq.ShouldBe(5);

        // The streaming path allocates sequence ids in blocks of batchSize, so the sequence may sit up
        // to batchSize-1 AHEAD of the tenant's max seq_id (a harmless gap) — but never behind it, which
        // is the corruption this pins (a live append re-issuing an already-used seq_id).
        await using (var cmd = new NpgsqlCommand(
                         $"select last_value from {_fixture.SchemaName}.mt_events_sequence_{tenant}", conn))
        {
            ((long)(await cmd.ExecuteScalarAsync())!).ShouldBeGreaterThanOrEqualTo(maxSeq);
        }

        // (c) A live append directly after the import continues above the imported events.
        await using (var session = _fixture.Store.LightweightSession(tenant))
        {
            session.Events.Append(streamX, new TripLeg(4.0));
            await session.SaveChangesAsync();
        }

        await using (var cmd = new NpgsqlCommand(
                         $"select max(seq_id) from {_fixture.SchemaName}.mt_events where tenant_id = @t", conn))
        {
            cmd.Parameters.AddWithValue("t", tenant);
            ((long)(await cmd.ExecuteScalarAsync())!).ShouldBeGreaterThan(maxSeq);
        }
    }
}
