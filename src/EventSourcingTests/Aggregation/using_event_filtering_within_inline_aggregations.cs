using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class using_event_filtering_within_inline_aggregations : OneOffConfigurationsContext
{
    public using_event_filtering_within_inline_aggregations()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);

            opts.Projections.Add<SimpleSingleStreamProjection>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task single_stream_projection_running_inline_only_applies_to_matching_events()
    {
        var simpleSingleStreamProjection = new SimpleSingleStreamProjection();
        simpleSingleStreamProjection.AllEventTypes.Contains(typeof(AEvent)).ShouldBeTrue();
        simpleSingleStreamProjection.AllEventTypes.Contains(typeof(BEvent)).ShouldBeTrue();
        simpleSingleStreamProjection.AllEventTypes.Contains(typeof(CEvent)).ShouldBeTrue();
        simpleSingleStreamProjection.AllEventTypes.Contains(typeof(DEvent)).ShouldBeTrue();

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var stream3 = Guid.NewGuid();
        var stream4 = Guid.NewGuid();

        var action1 = theSession.Events.StartStream(stream1, new AEvent(), new BEvent());
        var action2 = theSession.Events.StartStream(stream2, new QuestStarted(),
            new MembersJoined(1, "Emond's Field", "Mat", "Perrin", "Egwene"));


        simpleSingleStreamProjection.AppliesTo(action1.Events.Select(x => x.EventType)).ShouldBeTrue();
        new SingleStreamProjection<QuestParty, Guid>().AppliesTo(action1.Events.Select(x => x.EventType)).ShouldBeFalse();

        simpleSingleStreamProjection.AppliesTo(action2.Events.Select(x => x.EventType)).ShouldBeFalse();
        new SingleStreamProjection<QuestParty, Guid>().AppliesTo(action2.Events.Select(x => x.EventType)).ShouldBeTrue();

        await theSession.SaveChangesAsync();

        // Don't cross the streams...
        (await theSession.LoadAsync<QuestParty>(stream1)).ShouldBeNull();
        (await theSession.LoadAsync<QuestParty>(stream2)).ShouldNotBeNull();
        (await theSession.LoadAsync<MyAggregate>(stream1)).ShouldNotBeNull();
        (await theSession.LoadAsync<MyAggregate>(stream2)).ShouldBeNull();
    }

    [Fact]
    public async Task completely_ignore_event_that_does_not_apply_with_conventional_apply()
    {
        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var action1 = theSession.Events.StartStream(stream1, new QuestStarted(), new MembersJoined(1, "Emond's Field", "Mat", "Perrin", "Egwene"), new RandomNotUsedEvent());
        await theSession.SaveChangesAsync();

        theSession.Events.StartStream(stream2, new RandomNotUsedEvent(), new QuestStarted(), new MembersJoined(1, "Emond's Field", "Mat", "Perrin", "Egwene"));
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<QuestParty>(stream1)).ShouldNotBeNull();
        (await theSession.LoadAsync<QuestParty>(stream2)).ShouldNotBeNull();
    }
}

public record RandomNotUsedEvent;

public class SimpleSingleStreamProjection: SingleStreamProjection<MyAggregate, Guid>
{
    public SimpleSingleStreamProjection()
    {
        IncludeType<AEvent>();
        IncludeType<BEvent>();
        IncludeType<CEvent>();
        IncludeType<DEvent>();
    }

    public override MyAggregate Evolve(MyAggregate snapshot, Guid id, IEvent e)
    {
        snapshot ??= new();

        switch (e.Data)
        {
            case AEvent:
                snapshot.ACount++;
                break;
            case BEvent:
                snapshot.BCount++;
                break;
            case CEvent:
                snapshot.CCount++;
                break;
            case DEvent:
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}


