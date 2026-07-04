using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// JasperFx/marten#4806: bulk event append must preserve the ORIGINAL cross-stream order when migrating an
/// existing event log. The seq_id assigned to each event decides the global order the async daemon replays
/// in, so a multi-stream projection that compares Sequence across streams (e.g. "decisions appended before
/// this invoice was created") only works if migration keeps the interleaving. These tests cover both the
/// streaming import (<see cref="IDocumentStore.BulkInsertEventStreamAsync"/>) and the batch path, asserting
/// directly on the stored <c>mt_events</c> order — the thing that actually governs replay.
/// </summary>
public class BulkEventStreamAppendTests : OneOffConfigurationsContext
{
    public record Started(string Label);
    public record Progressed(int Step);
    public record Ended(string Label);

    // Register the event types up front (as the real migration does via its event registrations) so
    // ApplyAllConfiguredChangesToDatabaseAsync provisions the event tables — the streaming import builds
    // raw IEvents directly rather than going through StreamAction.Start, which would auto-register them.
    private DocumentStore StringIdentityStore() => StoreOptions(opts =>
    {
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.AddEventType<Started>();
        opts.Events.AddEventType<Progressed>();
        opts.Events.AddEventType<Ended>();
    });

    // Build a raw IEvent carrying its stream key + per-stream version, as a migration read would produce.
    private static IEvent EventFor(string streamKey, long version, object data)
    {
        var e = (IEvent)Activator.CreateInstance(typeof(Event<>).MakeGenericType(data.GetType()), data)!;
        e.StreamKey = streamKey;
        e.Version = version;
        e.Id = Guid.NewGuid();
        e.Sequence = version; // source seq is irrelevant to the streaming path (it uses arrival order)
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

    // Stored stream ids in seq_id order — the global order the async daemon will replay in.
    private static async Task<List<string>> StreamIdsBySeqAsync(DocumentStore store)
    {
        var ids = new List<string>();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select stream_id from {store.Options.Events.DatabaseSchemaName}.mt_events order by seq_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task<List<long>> VersionsForStreamAsync(DocumentStore store, string streamKey)
    {
        var versions = new List<long>();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select version from {store.Options.Events.DatabaseSchemaName}.mt_events where stream_id = @s order by seq_id",
            conn);
        cmd.Parameters.AddWithValue("s", streamKey);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }

    [Fact]
    public async Task streaming_import_preserves_cross_stream_order()
    {
        var store = StringIdentityStore();

        // Two streams, interleaved in the exact global order they occurred: A1, B1, A2, B2, A3.
        var ordered = new[]
        {
            EventFor("A", 1, new Started("A1")),
            EventFor("B", 1, new Started("B1")),
            EventFor("A", 2, new Progressed(1)),
            EventFor("B", 2, new Ended("B2")),
            EventFor("A", 3, new Ended("A3"))
        };

        var headers = new List<BulkEventStreamHeader>
        {
            new() { Key = "A", Version = 3 },
            new() { Key = "B", Version = 2 }
        };

        // batchSize 2 forces multiple COPY batches + a sequence-block refill mid-stream.
        await store.BulkInsertEventStreamAsync(StorageConstants.DefaultTenantId, headers, Stream(ordered),
            batchSize: 2, cancellation: CancellationToken.None);

        // The seq_id order (what the daemon replays in) must be the ORIGINAL interleaving, not per-stream
        // blocks (which would be A, A, A, B, B).
        (await StreamIdsBySeqAsync(store)).ShouldBe(new[] { "A", "B", "A", "B", "A" });

        // Per-stream versions preserved and contiguous.
        (await VersionsForStreamAsync(store, "A")).ShouldBe(new long[] { 1, 2, 3 });
        (await VersionsForStreamAsync(store, "B")).ShouldBe(new long[] { 1, 2 });
    }

    [Fact]
    public async Task streaming_import_refills_sequences_across_multiple_blocks()
    {
        // Regression for JasperFx/marten#4806: sequences are fetched one block (batchSize) at a time, so a
        // tenant with more events than batchSize empties the queue mid-stream and must refill. The refill runs
        // a nextval SELECT, which must NOT happen while the mt_events COPY is open on the same connection —
        // Npgsql rejects that with "connection is already in state 'Copy'". batchSize 2 with 7 events forces
        // several block boundaries + refills; the import must still complete with cross-stream order intact.
        var store = StringIdentityStore();

        var ordered = new[]
        {
            EventFor("A", 1, new Started("A1")),
            EventFor("B", 1, new Started("B1")),
            EventFor("A", 2, new Progressed(2)),
            EventFor("B", 2, new Progressed(2)),
            EventFor("A", 3, new Progressed(3)),
            EventFor("B", 3, new Ended("B3")),
            EventFor("A", 4, new Ended("A4"))
        };

        var headers = new List<BulkEventStreamHeader>
        {
            new() { Key = "A", Version = 4 },
            new() { Key = "B", Version = 3 }
        };

        await store.BulkInsertEventStreamAsync(StorageConstants.DefaultTenantId, headers, Stream(ordered),
            batchSize: 2, cancellation: CancellationToken.None);

        (await StreamIdsBySeqAsync(store)).ShouldBe(new[] { "A", "B", "A", "B", "A", "B", "A" });
        (await VersionsForStreamAsync(store, "A")).ShouldBe(new long[] { 1, 2, 3, 4 });
        (await VersionsForStreamAsync(store, "B")).ShouldBe(new long[] { 1, 2, 3 });
    }

    [Fact]
    public async Task batch_import_assigns_seq_ids_in_source_order_across_streams()
    {
        var store = StringIdentityStore();

        // Two stream actions; simulate a migration by stamping each event's SOURCE sequence so the global
        // order is interleaved A1, B1, A2, B2, A3 even though the events are grouped per stream.
        var actionA = StreamAction.Start(store.Events, "A",
            new object[] { new Started("A1"), new Progressed(1), new Ended("A3") });
        var actionB = StreamAction.Start(store.Events, "B",
            new object[] { new Started("B1"), new Ended("B2") });

        actionA.Events[0].Sequence = 1;
        actionB.Events[0].Sequence = 2;
        actionA.Events[1].Sequence = 3;
        actionB.Events[1].Sequence = 4;
        actionA.Events[2].Sequence = 5;

        await store.BulkInsertEventsAsync(new[] { actionA, actionB });

        // Target seq_id order must honor the source interleaving, not each stream's contiguous block.
        (await StreamIdsBySeqAsync(store)).ShouldBe(new[] { "A", "B", "A", "B", "A" });
    }

    [Fact]
    public async Task batch_import_of_freshly_created_streams_is_unchanged()
    {
        // Fresh seeding (no meaningful source Sequence, all 0): the stable sort must keep the per-stream
        // order the caller passed, i.e. behavior identical to before the ordering fix.
        var store = StringIdentityStore();

        var actionA = StreamAction.Start(store.Events, "A",
            new object[] { new Started("A1"), new Ended("A2") });
        var actionB = StreamAction.Start(store.Events, "B",
            new object[] { new Started("B1"), new Ended("B2") });

        await store.BulkInsertEventsAsync(new[] { actionA, actionB });

        (await StreamIdsBySeqAsync(store)).ShouldBe(new[] { "A", "A", "B", "B" });
        (await VersionsForStreamAsync(store, "A")).ShouldBe(new long[] { 1, 2 });
        (await VersionsForStreamAsync(store, "B")).ShouldBe(new long[] { 1, 2 });
    }
}
