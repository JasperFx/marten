using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class replacing_events : OneOffConfigurationsContext
{
    [Fact]
    public async Task simple_replacement_default_settings()
    {
        var streamId = Guid.NewGuid();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theSession.Events.StartStream<Quest>(streamId, joined, departed);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        var sequence = events.Last().Sequence;

        var joined2 = new MembersJoined { Members = ["Moiraine", "Lan"] };
        theSession.Events.CompletelyReplaceEvent(sequence, joined2);
        await theSession.SaveChangesAsync();

        var events2 = await theSession.Events.FetchStreamAsync(streamId);
        var final = events2.Last().ShouldBeOfType<Event<MembersJoined>>();

        // These should not change
        final.Version.ShouldBe(events.Last().Version);
        final.Sequence.ShouldBe(events.Last().Sequence);

        // Id gets changed
        final.Id.ShouldNotBe(events.Last().Id);

        // These need to get changed
        final.Data.Members.ShouldBe(["Moiraine", "Lan"]);
        final.DotNetTypeName.ShouldBe("EventSourcingTests.MembersJoined, EventSourcingTests");
        final.EventTypeName.ShouldBe("members_joined");

    }

    [Fact]
    public async Task simple_replacement_all_metadata_turned_on()
    {
        StoreOptions(opts =>
        {
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
        });

        var streamId = Guid.NewGuid();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theSession.Events.StartStream<Quest>(streamId, joined, departed);
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        var sequence = events.Last().Sequence;

        var joined2 = new MembersJoined { Members = ["Moiraine", "Lan"] };
        theSession.Events.CompletelyReplaceEvent(sequence, joined2);
        await theSession.SaveChangesAsync();

        var events2 = await theSession.Events.FetchStreamAsync(streamId);
        var final = events2.Last().ShouldBeOfType<Event<MembersJoined>>();

        // These should not change
        final.Version.ShouldBe(events.Last().Version);
        final.Sequence.ShouldBe(events.Last().Sequence);

        // Id gets changed
        final.Id.ShouldNotBe(events.Last().Id);
        final.Timestamp.ShouldNotBe(events.Last().Timestamp);

        // These need to get changed
        final.Data.Members.ShouldBe(["Moiraine", "Lan"]);
        final.DotNetTypeName.ShouldBe("EventSourcingTests.MembersJoined, EventSourcingTests");
        final.EventTypeName.ShouldBe("members_joined");

    }
}
