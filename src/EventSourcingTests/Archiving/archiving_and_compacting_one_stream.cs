using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Archiving;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Archiving;

/// <summary>
/// marten#6048: the "Shrinking mt_events" docs page says archiving and compacting work together in one order
/// only. These pin both orders.
/// </summary>
public class archiving_and_compacting_one_stream: OneOffConfigurationsContext
{
    public record ShipmentCreated(string Destination);

    public record ShipmentMoved(string Location);

    public record ShipmentDelivered;

    public class Shipment
    {
        public Guid Id { get; set; }
        public string Destination { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public int Moves { get; set; }
        public bool Delivered { get; set; }

        public static Shipment Create(ShipmentCreated e) => new() { Destination = e.Destination };

        public void Apply(ShipmentMoved e)
        {
            Location = e.Location;
            Moves++;
        }

        public void Apply(ShipmentDelivered _) => Delivered = true;
    }

    private async Task<Guid> aDeliveredShipment()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Shipment>(id, new ShipmentCreated("Utrecht"), new ShipmentMoved("Rotterdam"),
            new ShipmentMoved("Gouda"), new ShipmentMoved("Utrecht"), new ShipmentDelivered());
        await session.SaveChangesAsync();
        return id;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task compact_a_finished_stream_then_archive_it(bool partitioned)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = partitioned);

        var streamId = await aDeliveredShipment();

        #region sample_compact_then_archive_a_stream

        await using var session = theStore.LightweightSession();

        // Compact first. Compacting only reads the stream's live events, so it
        // has to happen before the archive. The five events become one
        // Compacted<Shipment> event at version 5
        await session.Events.CompactStreamAsync<Shipment>(streamId);
        await session.SaveChangesAsync();

        // Then archive the stream. Its one remaining event moves out of the
        // active event table, or is marked archived without partitioning
        session.Events.ArchiveStream(streamId);
        await session.SaveChangesAsync();

        #endregion

        var events = await session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId)
            .ToListAsync();

        var compacted = events.Single();
        compacted.IsArchived.ShouldBeTrue();
        compacted.Version.ShouldBe(5);

        var snapshot = compacted.Data.ShouldBeOfType<Compacted<Shipment>>().Snapshot;
        snapshot.Moves.ShouldBe(3);
        snapshot.Delivered.ShouldBeTrue();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.IsArchived.ShouldBeTrue();
        state.Version.ShouldBe(5);
        state.CompactedVersion.ShouldBe(5);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task compacting_an_archived_stream_does_nothing(bool partitioned)
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = partitioned);

        var streamId = await aDeliveredShipment();

        await using var session = theStore.LightweightSession();
        session.Events.ArchiveStream(streamId);
        await session.SaveChangesAsync();

        await session.Events.CompactStreamAsync<Shipment>(streamId);
        await session.SaveChangesAsync();

        var events = await session.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == streamId)
            .ToListAsync();

        events.Count.ShouldBe(5);
        events.ShouldAllBe(x => x.IsArchived);
        events.ShouldNotContain(x => x.Data is Compacted<Shipment>);

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.CompactedVersion.ShouldBe(0);
    }

    [Fact]
    public async Task a_compacted_stream_still_takes_appends_and_an_archived_one_does_not()
    {
        var compactedId = await aDeliveredShipment();
        var archivedId = await aDeliveredShipment();

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Shipment>(compactedId);
            session.Events.ArchiveStream(archivedId);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(compactedId, new ShipmentMoved("Amersfoort"));
            await session.SaveChangesAsync();

            var shipment = await session.Events.AggregateStreamAsync<Shipment>(compactedId);
            shipment.ShouldNotBeNull();
            shipment.Moves.ShouldBe(4);
            shipment.Location.ShouldBe("Amersfoort");
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(archivedId, new ShipmentMoved("Amersfoort"));
            await Should.ThrowAsync<ArchivedStreamException>(() => session.SaveChangesAsync());
        }
    }
}
