using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.MemoryPack;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

[Collection("Marten.MemoryPack")]
public class MemoryPackEventTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "memorypack_events";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;

            // #4515 Phase 1: binary serialization currently only supported on
            // the Rich append path. Force-Rich here; the Quick descriptor
            // builders throw with a clear remediation message if a binary
            // event type is registered while AppendMode is non-Rich.
            opts.Events.AppendMode = EventAppendMode.Rich;

            // Wire MemoryPack as the store-wide fallback for [BinaryEvent]
            // types. TripStarted/PassengerPickedUp/TripEnded resolve to this
            // serializer through the attribute.
            opts.Events.UseMemoryPackSerializer();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task can_round_trip_a_single_binary_event()
    {
        var streamId = Guid.NewGuid();
        var started = new TripStarted(streamId, "Alice", DateTimeOffset.UtcNow);

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId, started);
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(1);
        var loaded = events[0].Data.ShouldBeOfType<TripStarted>();
        loaded.TripId.ShouldBe(streamId);
        loaded.DriverName.ShouldBe("Alice");
        loaded.StartedAt.ShouldBe(started.StartedAt);
    }

    [Fact]
    public async Task multiple_binary_events_replay_in_order()
    {
        var streamId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Bob", startedAt),
                new PassengerPickedUp(streamId, "Carol", startedAt.AddMinutes(5)),
                new TripEnded(streamId, startedAt.AddMinutes(30), 24.50m));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(3);
        events[0].Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("Bob");
        events[1].Data.ShouldBeOfType<PassengerPickedUp>().PassengerName.ShouldBe("Carol");
        events[2].Data.ShouldBeOfType<TripEnded>().FareAmount.ShouldBe(24.50m);
    }

    // The flagship test: the same stream carries JSON-serialized events and
    // binary-serialized events. Round-trip must work for both, demonstrating
    // the per-row dispatch (bdata IS NULL ⇒ JSON path; non-null ⇒ binary).
    [Fact]
    public async Task json_and_binary_events_coexist_on_one_stream()
    {
        var streamId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Dana", DateTimeOffset.UtcNow), // binary
                new TripCommentAdded(streamId, "looking good", DateTimeOffset.UtcNow), // JSON
                new PassengerPickedUp(streamId, "Eli", DateTimeOffset.UtcNow), // binary
                new TripCommentAdded(streamId, "passenger boarded", DateTimeOffset.UtcNow)); // JSON
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(4);
        events[0].Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("Dana");
        events[1].Data.ShouldBeOfType<TripCommentAdded>().Comment.ShouldBe("looking good");
        events[2].Data.ShouldBeOfType<PassengerPickedUp>().PassengerName.ShouldBe("Eli");
        events[3].Data.ShouldBeOfType<TripCommentAdded>().Comment.ShouldBe("passenger boarded");
    }

    [Fact]
    public async Task binary_events_land_in_bdata_column_jsoned_events_land_in_data_column()
    {
        var streamId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Frank", DateTimeOffset.UtcNow), // binary
                new TripCommentAdded(streamId, "hello", DateTimeOffset.UtcNow)); // JSON
            await session.SaveChangesAsync();
        }

        // Pull the raw column values to verify the on-disk shape — the
        // discriminator is bdata IS NULL.
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select type, bdata is null as bdata_is_null, data::text " +
                          "from memorypack_events.mt_events where stream_id = $1 order by version";
        var p = cmd.CreateParameter();
        p.Value = streamId;
        cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new System.Collections.Generic.List<(string type, bool bdataIsNull, string data)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        }

        rows.Count.ShouldBe(2);

        // First event is binary — bdata IS NOT NULL, data is the {} placeholder
        rows[0].type.ShouldBe("trip_started");
        rows[0].bdataIsNull.ShouldBeFalse();
        rows[0].data.ShouldBe("{}");

        // Second event is JSON — bdata IS NULL, data has the real payload
        rows[1].type.ShouldBe("trip_comment_added");
        rows[1].bdataIsNull.ShouldBeTrue();
        rows[1].data.ShouldContain("hello");
    }

    // Upgrade-backfill: simulate a row written *before* the bdata column
    // existed (NULL bdata, real JSON in data) and confirm it still reads
    // through the JSON path after the feature is in place. This is what
    // makes the migration safe — no event data needs converting.
    [Fact]
    public async Task pre_existing_json_rows_still_read_after_feature_is_in_place()
    {
        var streamId = Guid.NewGuid();

        // Write through the standard JSON path first (a non-binary event type).
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripCommentAdded(streamId, "pre-upgrade comment", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // The row is in mt_events with bdata = NULL. Read it back — the
        // per-row dispatch should fall through to mapping.ReadEventData.
        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<TripCommentAdded>()
            .Comment.ShouldBe("pre-upgrade comment");
    }
}

// Forces both binary test classes into one collection so they don't
// race on the shared mt_events schema; mirrors the Marten.PgVector
// pattern from PR #4576.
[CollectionDefinition("Marten.MemoryPack", DisableParallelization = true)]
public class MemoryPackCollection;

// #4515 upgrade-path test: prove the *changeover* works on an existing
// store. Two DocumentStore instances point at the same database with the
// same schema name; the first is configured without any binary
// serialization, the second adds `opts.Events.UseMemoryPackSerializer()`
// (the only delta). The test isn't reusing the fixture's shared store
// because each phase needs its own DocumentStore lifecycle.
[Collection("Marten.MemoryPack")]
public class binary_serialization_upgrade_tests
{
    private const string Schema = "memorypack_upgrade";

    [Fact]
    public async Task enabling_binary_serialization_on_existing_store_still_reads_old_json_events_and_appends_new_binary()
    {
        var streamId = Guid.NewGuid();

        // ----- Phase 1 ---------------------------------------------------
        // Store with NO binary serialization wired. Uses a JSON-only event
        // type. This is what an existing production system looks like before
        // the binary opt-in is added.
        await using (var storeBeforeBinary = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = Schema;
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            // Rich on purpose so the second-phase store can also be Rich without
            // re-doing the schema bind ordering — kept identical to make the
            // "only delta" promise visible. (If a future user has Quick on
            // Phase 1, they'd hit AssertNoBinaryEventsForQuickMode when adding
            // binary events in Phase 2 — that's the documented Phase-2 follow-up.)
            opts.Events.AppendMode = EventAppendMode.Rich;
        }))
        {
            // Clean slate — drop any leftover state from a prior test run.
            await storeBeforeBinary.Advanced.Clean.CompletelyRemoveAllAsync();
            await storeBeforeBinary.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            await using var session = storeBeforeBinary.LightweightSession();
            session.Events.StartStream(streamId,
                new TripCommentAdded(streamId, "from json-only store v1", DateTimeOffset.UtcNow),
                new TripCommentAdded(streamId, "from json-only store v2", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // ----- Phase 2 ---------------------------------------------------
        // Same config except UseMemoryPackSerializer() is now wired. The
        // [BinaryEvent]-marked types (TripStarted / TripEnded) resolve to
        // MemoryPack at EventMapping construction.
        await using (var storeWithBinary = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = Schema;                     // SAME schema
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.Events.AppendMode = EventAppendMode.Rich;
            opts.Events.UseMemoryPackSerializer();                // THE only delta
        }))
        {
            // Schema apply — no-op for `data` / `bdata` (column already exists
            // from Phase 1's migration; existing rows have bdata = NULL).
            await storeWithBinary.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            // Append new BINARY events to the SAME stream that already holds
            // two JSON-serialized events from Phase 1.
            await using (var session = storeWithBinary.LightweightSession())
            {
                session.Events.Append(streamId,
                    new TripStarted(streamId, "Alice", DateTimeOffset.UtcNow),
                    new TripEnded(streamId, DateTimeOffset.UtcNow, 42.50m));
                await session.SaveChangesAsync();
            }

            // Read back the whole stream — must replay all four events
            // in the correct order, JSON and binary deserializing through
            // the per-row dispatch on bdata IS NULL.
            await using (var query = storeWithBinary.QuerySession())
            {
                var events = await query.Events.FetchStreamAsync(streamId);

                events.Count.ShouldBe(4);

                // Phase 1 JSON events
                events[0].Data.ShouldBeOfType<TripCommentAdded>()
                    .Comment.ShouldBe("from json-only store v1");
                events[1].Data.ShouldBeOfType<TripCommentAdded>()
                    .Comment.ShouldBe("from json-only store v2");

                // Phase 2 binary events
                events[2].Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("Alice");
                events[3].Data.ShouldBeOfType<TripEnded>().FareAmount.ShouldBe(42.50m);

                // Version numbers must be monotonic across the format switch
                // — the Phase 2 store has to see the existing stream
                // version (2) and assign 3, 4 to the new events.
                events.Select(e => e.Version).ShouldBe(new long[] { 1, 2, 3, 4 });
            }

            // Belt-and-braces: also append a NEW JSON event (the JSON
            // path is supposed to keep working in the same store, on the
            // same stream, alongside binary appends).
            await using (var session = storeWithBinary.LightweightSession())
            {
                session.Events.Append(streamId,
                    new TripCommentAdded(streamId, "post-upgrade JSON", DateTimeOffset.UtcNow));
                await session.SaveChangesAsync();
            }

            await using (var query = storeWithBinary.QuerySession())
            {
                var events = await query.Events.FetchStreamAsync(streamId);
                events.Count.ShouldBe(5);
                events[4].Data.ShouldBeOfType<TripCommentAdded>()
                    .Comment.ShouldBe("post-upgrade JSON");
            }

            // Verify the on-disk shape — the JSON rows have bdata NULL,
            // the binary rows don't. This is the row-level discriminator
            // the read path keys off of.
            await using (var conn = storeWithBinary.Storage.Database.CreateConnection())
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"select type, bdata is null as bdata_is_null " +
                                  $"from {Schema}.mt_events where stream_id = $1 order by version";
                var p = cmd.CreateParameter();
                p.Value = streamId;
                cmd.Parameters.Add(p);

                var rows = new System.Collections.Generic.List<(string type, bool bdataIsNull)>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add((reader.GetString(0), reader.GetBoolean(1)));
                }

                rows[0].ShouldBe(("trip_comment_added", true));   // phase 1 JSON
                rows[1].ShouldBe(("trip_comment_added", true));   // phase 1 JSON
                rows[2].ShouldBe(("trip_started", false));        // phase 2 binary
                rows[3].ShouldBe(("trip_ended", false));          // phase 2 binary
                rows[4].ShouldBe(("trip_comment_added", true));   // phase 2 JSON
            }
        }
    }
}
