using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.MemoryPack;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

#region trip aggregate + projection — exercises binary events through Marten's standard aggregation path

public class Trip
{
    public Guid Id { get; set; }
    public string DriverName { get; set; } = "";
    // Setters required so the Trip snapshot round-trips through JSON
    // serialization on the inline-projection path.
    public List<string> Passengers { get; set; } = new();
    public decimal FareAmount { get; set; }
    public bool IsCompleted { get; set; }
    public List<string> Comments { get; set; } = new();

    public void Apply(TripStarted e)
    {
        Id = e.TripId;
        DriverName = e.DriverName;
    }

    public void Apply(PassengerPickedUp e) => Passengers.Add(e.PassengerName);

    public void Apply(TripEnded e)
    {
        FareAmount = e.FareAmount;
        IsCompleted = true;
    }

    public void Apply(TripCommentAdded e) => Comments.Add(e.Comment);
}

#endregion

// Integration tests for binary event serialization (#4515) across the
// read-side surface Marten consumers actually use: live aggregation,
// FetchForWriting (DCB / read-modify-write), and inline projections.
// The shared per-row Resolve/ResolveAsync dispatch from #4578 is the
// load-bearing piece; if these tests pass for binary events the
// dispatch is sound across the full Marten surface.
[Collection("Marten.MemoryPack")]
public class BinaryEventIntegrationTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "memorypack_projections";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.Events.AppendMode = EventAppendMode.Rich;

            // Wire MemoryPack as the store-wide fallback so [BinaryEvent]
            // types (TripStarted / PassengerPickedUp / TripEnded) resolve to
            // the binary serializer at EventMapping construction.
            opts.Events.UseMemoryPackSerializer();

            // Inline self-aggregation — Trip's conventional Apply methods
            // are dispatched as the projection. Same trigger model as a
            // SingleStreamProjection<Trip>: runs in the SaveChanges transaction,
            // so LoadAsync<Trip>(streamId) right after SaveChangesAsync
            // returns the projected document.
            opts.Projections.Snapshot<Trip>(SnapshotLifecycle.Inline);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    // Live aggregation over a stream of binary events — Resolve must
    // dispatch each row to the binary deserializer and the aggregator
    // must see the typed event Data instances.
    [Fact]
    public async Task aggregate_stream_async_replays_binary_events()
    {
        var tripId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(tripId,
                new TripStarted(tripId, "Alice", startedAt),
                new PassengerPickedUp(tripId, "Bob", startedAt.AddMinutes(2)),
                new PassengerPickedUp(tripId, "Carol", startedAt.AddMinutes(5)),
                new TripEnded(tripId, startedAt.AddMinutes(30), 42.50m));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.Events.AggregateStreamAsync<Trip>(tripId);

        trip.ShouldNotBeNull();
        trip.Id.ShouldBe(tripId);
        trip.DriverName.ShouldBe("Alice");
        trip.Passengers.ShouldBe(new[] { "Bob", "Carol" });
        trip.FareAmount.ShouldBe(42.50m);
        trip.IsCompleted.ShouldBeTrue();
    }

    // Live aggregation over a mixed-format stream — binary TripStarted +
    // JSON TripCommentAdded + binary TripEnded. Each row goes through
    // its own deserialization path; the aggregator stays agnostic.
    [Fact]
    public async Task aggregate_stream_async_replays_mixed_binary_and_json_events()
    {
        var tripId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(tripId,
                new TripStarted(tripId, "Dana", DateTimeOffset.UtcNow),         // binary
                new TripCommentAdded(tripId, "leaving depot", DateTimeOffset.UtcNow), // JSON
                new PassengerPickedUp(tripId, "Eli", DateTimeOffset.UtcNow),    // binary
                new TripCommentAdded(tripId, "passenger on board", DateTimeOffset.UtcNow), // JSON
                new TripEnded(tripId, DateTimeOffset.UtcNow, 19.00m));          // binary
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.Events.AggregateStreamAsync<Trip>(tripId);

        trip.ShouldNotBeNull();
        trip.DriverName.ShouldBe("Dana");
        trip.Passengers.ShouldBe(new[] { "Eli" });
        trip.Comments.ShouldBe(new[] { "leaving depot", "passenger on board" });
        trip.FareAmount.ShouldBe(19.00m);
        trip.IsCompleted.ShouldBeTrue();
    }

    // Inline projection — every save runs the projection synchronously in
    // the same transaction. Reading the projected Trip document right
    // after SaveChangesAsync should reflect the binary events.
    [Fact]
    public async Task inline_projection_applies_binary_events()
    {
        var tripId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Trip>(tripId,
                new TripStarted(tripId, "Frank", DateTimeOffset.UtcNow),
                new PassengerPickedUp(tripId, "Gina", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.LoadAsync<Trip>(tripId);

        trip.ShouldNotBeNull();
        trip.DriverName.ShouldBe("Frank");
        trip.Passengers.ShouldBe(new[] { "Gina" });
        trip.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task inline_projection_updates_across_two_appends_with_binary_events()
    {
        var tripId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Trip>(tripId,
                new TripStarted(tripId, "Hana", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(tripId,
                new PassengerPickedUp(tripId, "Ian", DateTimeOffset.UtcNow),
                new TripEnded(tripId, DateTimeOffset.UtcNow, 12.00m));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.LoadAsync<Trip>(tripId);

        trip.ShouldNotBeNull();
        trip.DriverName.ShouldBe("Hana");
        trip.Passengers.ShouldBe(new[] { "Ian" });
        trip.FareAmount.ShouldBe(12.00m);
        trip.IsCompleted.ShouldBeTrue();
    }

    // FetchForWriting (DCB / read-modify-write) over binary events —
    // proves the optimistic-concurrency read path goes through the same
    // per-row dispatch and that subsequent appends carry the right
    // version number forward.
    [Fact]
    public async Task fetch_for_writing_round_trips_binary_events()
    {
        var tripId = Guid.NewGuid();

        // Seed the stream
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Trip>(tripId,
                new TripStarted(tripId, "Jordan", DateTimeOffset.UtcNow),
                new PassengerPickedUp(tripId, "Kim", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        // Read-modify-write — FetchForWriting hydrates the aggregate
        // from the binary-serialized events, we examine state, then
        // append a new binary event on top.
        await using (var session = _store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Trip>(tripId);

            stream.Aggregate.ShouldNotBeNull();
            stream.Aggregate.DriverName.ShouldBe("Jordan");
            stream.Aggregate.Passengers.ShouldBe(new[] { "Kim" });

            stream.AppendOne(new TripEnded(tripId, DateTimeOffset.UtcNow, 27.75m));
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.Events.AggregateStreamAsync<Trip>(tripId);
        trip.ShouldNotBeNull();
        trip.IsCompleted.ShouldBeTrue();
        trip.FareAmount.ShouldBe(27.75m);
    }
}
