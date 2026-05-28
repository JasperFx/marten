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
