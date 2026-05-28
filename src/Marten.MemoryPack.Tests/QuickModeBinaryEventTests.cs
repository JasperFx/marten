using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.MemoryPack;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

// #4515 Phase 2: binary event serialization works on the Quick + Quick
// with-server-timestamps append paths too, not just Rich. The
// mt_quick_append_events server function gained a `bdatas bytea[]`
// parameter that's inserted into mt_events.bdata in parallel with bodies.
// These tests pin the per-event-type dispatch on both Quick variants.
[Collection("Marten.MemoryPack")]
public class QuickModeBinaryEventTests
{
    [Fact]
    public async Task quick_with_server_timestamps_round_trips_binary_events()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "memorypack_quick_sts";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            // Default append mode — no Rich override.
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseMemoryPackSerializer();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Alice", DateTimeOffset.UtcNow),     // binary
                new TripCommentAdded(streamId, "leaving", DateTimeOffset.UtcNow), // JSON
                new TripEnded(streamId, DateTimeOffset.UtcNow, 33.00m));       // binary
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(3);
        events[0].Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("Alice");
        events[1].Data.ShouldBeOfType<TripCommentAdded>().Comment.ShouldBe("leaving");
        events[2].Data.ShouldBeOfType<TripEnded>().FareAmount.ShouldBe(33.00m);
    }

    [Fact]
    public async Task quick_mode_round_trips_binary_events()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "memorypack_quick_plain";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseMemoryPackSerializer();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Bob", DateTimeOffset.UtcNow),
                new PassengerPickedUp(streamId, "Carol", DateTimeOffset.UtcNow),
                new TripEnded(streamId, DateTimeOffset.UtcNow, 15.50m));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);

        events.Count.ShouldBe(3);
        events[0].Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("Bob");
        events[1].Data.ShouldBeOfType<PassengerPickedUp>().PassengerName.ShouldBe("Carol");
        events[2].Data.ShouldBeOfType<TripEnded>().FareAmount.ShouldBe(15.50m);
    }

    // On-disk shape verification — binary events under Quick mode produce
    // the same row shape as under Rich mode: data is the {} placeholder,
    // bdata holds the bytes.
    [Fact]
    public async Task quick_mode_binary_events_land_in_bdata_column()
    {
        const string schema = "memorypack_quick_shape";

        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseMemoryPackSerializer();
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new TripStarted(streamId, "Dana", DateTimeOffset.UtcNow),         // binary
                new TripCommentAdded(streamId, "hello", DateTimeOffset.UtcNow));  // JSON
            await session.SaveChangesAsync();
        }

        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select type, bdata is null as bdata_is_null, data::text " +
                          $"from {schema}.mt_events where stream_id = $1 order by version";
        var p = cmd.CreateParameter();
        p.Value = streamId;
        cmd.Parameters.Add(p);

        var rows = new System.Collections.Generic.List<(string type, bool bdataIsNull, string data)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        }

        rows.Count.ShouldBe(2);
        rows[0].type.ShouldBe("trip_started");
        rows[0].bdataIsNull.ShouldBeFalse();
        rows[0].data.ShouldBe("{}");
        rows[1].type.ShouldBe("trip_comment_added");
        rows[1].bdataIsNull.ShouldBeTrue();
        rows[1].data.ShouldContain("hello");
    }
}
