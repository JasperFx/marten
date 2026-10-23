using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.MemoryPack.Tests;

[Collection("Marten.MemoryPack")]
public class overwriting_binary_events: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "memorypack_masking";
            opts.Events.UseMemoryPackSerializer();
            opts.Events.AddMaskingRuleForProtectedInformation<TripStarted>(x => x with { DriverName = "****" });
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return default;
    }

    [Fact]
    public async Task masking_a_binary_event_masks_what_is_read_back()
    {
        var tripId = Guid.NewGuid();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(tripId, new TripStarted(tripId, "Alice", DateTimeOffset.UtcNow));
            await session.SaveChangesAsync();
        }

        await _store.Advanced.ApplyEventDataMasking(x => x.IncludeStream(tripId));

        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(tripId);
        events.Single().Data.ShouldBeOfType<TripStarted>().DriverName.ShouldBe("****");
    }

    [Fact]
    public async Task compacting_a_stream_of_binary_events_reads_back_the_snapshot()
    {
        var tripId = Guid.NewGuid();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(tripId,
                new TripStarted(tripId, "Alice", DateTimeOffset.UtcNow),
                new PassengerPickedUp(tripId, "Bob", DateTimeOffset.UtcNow),
                new TripEnded(tripId, DateTimeOffset.UtcNow, 12.5m));
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Trip>(tripId);
            await session.SaveChangesAsync();
        }

        await using var query = _store.QuerySession();
        var trip = await query.Events.AggregateStreamAsync<Trip>(tripId);
        trip.ShouldNotBeNull();
        trip.DriverName.ShouldBe("Alice");
        trip.Passengers.ShouldBe(["Bob"]);
        trip.FareAmount.ShouldBe(12.5m);
    }
}
