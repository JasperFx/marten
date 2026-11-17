using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_6032_rebuild_keeps_a_compacted_streams_natural_key: OneOffConfigurationsContext
{
    public sealed record TicketNumber(string Value);

    public sealed record TicketOpened(Guid TicketId, string Number);

    public sealed record TicketTouched(int Count);

    public sealed class Ticket
    {
        public Guid Id { get; set; }

        [NaturalKey]
        public TicketNumber Number { get; set; } = null!;

        public int Touches { get; set; }

        [NaturalKeySource]
        public static Ticket Create(TicketOpened e) => new() { Id = e.TicketId, Number = new TicketNumber(e.Number) };

        public void Apply(TicketTouched e) => Touches += e.Count;
    }

    [Fact]
    public async Task rebuild_after_compaction_keeps_the_natural_key()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
            opts.Projections.Snapshot<Ticket>(SnapshotLifecycle.Inline);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();
        var number = "T-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Ticket>(streamId, new TicketOpened(streamId, number), new TicketTouched(1),
                new TicketTouched(2));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Ticket>(streamId);
            await session.SaveChangesAsync();
        }

        // Keep appending after the compaction. A stream whose last event is the Compacted<T> fails the
        // rebuild outright with a NullReferenceException (#6036), which is a separate problem.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(streamId, new TicketTouched(4));
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            (await query.Events.FetchLatest<Ticket, TicketNumber>(new TicketNumber(number))).ShouldNotBeNull();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Ticket>(CancellationToken.None);

        await using (var query = theStore.LightweightSession())
        {
            var ticket = await query.Events.FetchLatest<Ticket, TicketNumber>(new TicketNumber(number));
            ticket.ShouldNotBeNull();
            ticket.Touches.ShouldBe(7);
        }
    }
}
