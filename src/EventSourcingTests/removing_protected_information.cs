using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class removing_protected_information : OneOffConfigurationsContext
{
    private readonly EventGraph theEvents = new EventGraph(new StoreOptions());

    [Fact]
    public void miss_on_masking()
    {
        theEvents.TryMask(new Event<AEvent>(new AEvent()))
            .ShouldBeFalse();
    }

    [Fact]
    public void match_exactly_on_event_type()
    {
        theEvents.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "****");

        var started = new QuestStarted { Name = "Find the Eye of the World" };

        var @event = new Event<QuestStarted>(started);

        theEvents.TryMask(@event).ShouldBeTrue();

        started.Name.ShouldBe("****");

    }

    [Fact]
    public void apply_with_contra_variance()
    {
        theEvents.AddMaskingRuleForProtectedInformation<IAccountEvent>(x => x.Name = "****");

        var changed = new AccountChanged { Name = "Harry" };

        var @event = new Event<AccountChanged>(changed);

        theEvents.TryMask(@event).ShouldBeTrue();

        changed.Name.ShouldBe("****");
    }

    [Fact]
    public async Task end_to_end_for_Guid_identified_stream_single_rule()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "*****");
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        theSession.SetHeader("color", "blue");

        var streamId =
            theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var streamId2 = theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find the Eye of the World"}, new MembersJoined(1, "Fal Dara", "Rand", "Perrin", "Mat")).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking()
            .IncludeStream(streamId)
            .AddHeader("color", "green")
            .AddHeader("opid", 1)
            .ApplyAsync();

        // Should apply to this stream
        var events = await theSession.Events.FetchStreamAsync(streamId);
        var questStarted = events.OfType<IEvent<QuestStarted>>().Single();
        questStarted.Data.Name.ShouldBe("*****");
        questStarted.Headers["color"].ShouldBe("green");
        questStarted.Headers["opid"].ShouldBe(1);

        // Just proving that the headers were not modified
        // on events that do not get masked
        foreach (var @event in events.Skip(1))
        {
            @event.Headers["color"].ShouldBe("blue");
        }

        // Should *not* apply here
        (await theSession.Events.FetchStreamAsync(streamId2)).Select(x => x.Data).OfType<QuestStarted>()
            .Single().Name.ShouldNotBe("*****");
    }

    [Fact]
    public async Task end_to_end_for_Guid_identified_stream_multiple_rules_and_single_stream()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "*****");
            opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
            {
                for (int i = 0; i < x.Members.Length; i++)
                {
                    x.Members[i] = "*****";
                }
            });
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        theSession.SetHeader("color", "blue");

        var streamId =
            theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var streamId2 = theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find the Eye of the World"}, new MembersJoined(1, "Fal Dara", "Rand", "Perrin", "Mat")).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking()
            .IncludeStream(streamId)
            .AddHeader("color", "green")
            .AddHeader("opid", 1)
            .ApplyAsync();

        // Should apply to this stream
        var events = await theSession.Events.FetchStreamAsync(streamId);
        var questStarted = events.OfType<IEvent<QuestStarted>>().Single();
        questStarted.Data.Name.ShouldBe("*****");
        questStarted.Headers["color"].ShouldBe("green");
        questStarted.Headers["opid"].ShouldBe(1);

        var joined = events.OfType<Event<MembersJoined>>().Single();
        foreach (var memberName in joined.Data.Members)
        {
            memberName.ShouldBe("*****");
        }

        joined.Headers["color"].ShouldBe("green");
        joined.Headers["opid"].ShouldBe(1);

        // The last event does not get masked
        events.Last().Headers["color"].ShouldBe("blue");

        // Should *not* apply here
        var events2 = await theSession.Events.FetchStreamAsync(streamId2);
        events2.Select(x => x.Data).OfType<QuestStarted>()
            .Single().Name.ShouldNotBe("*****");
    }

    [Fact]
    public async Task end_to_end_for_Guid_identified_stream_multiple_rules_and_multiple_streams()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "*****");
            opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
            {
                for (int i = 0; i < x.Members.Length; i++)
                {
                    x.Members[i] = "*****";
                }
            });
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        theSession.SetHeader("color", "blue");

        var streamId =
            theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var streamId2 = theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find the Eye of the World"}, new MembersJoined(1, "Fal Dara", "Rand", "Perrin", "Mat")).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking()
            .IncludeStream(streamId)
            .IncludeStream(streamId2)
            .AddHeader("color", "green")
            .AddHeader("opid", 1)
            .ApplyAsync();

        // Should apply to this stream
        var events = await theSession.Events.FetchStreamAsync(streamId);
        var questStarted = events.OfType<IEvent<QuestStarted>>().Single();
        questStarted.Data.Name.ShouldBe("*****");
        questStarted.Headers["color"].ShouldBe("green");
        questStarted.Headers["opid"].ShouldBe(1);

        var joined = events.OfType<Event<MembersJoined>>().Single();
        foreach (var memberName in joined.Data.Members)
        {
            memberName.ShouldBe("*****");
        }

        joined.Headers["color"].ShouldBe("green");
        joined.Headers["opid"].ShouldBe(1);

        // The last event does not get masked
        events.Last().Headers["color"].ShouldBe("blue");

        // Should *not* apply here
        var events2 = await theSession.Events.FetchStreamAsync(streamId2);
        events2.Select(x => x.Data).OfType<QuestStarted>()
            .Single().Name.ShouldBe("*****");
    }

        [Fact]
    public async Task end_to_end_for_Guid_identified_stream_multiple_rules_and_through_filter()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "*****");
            opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
            {
                for (int i = 0; i < x.Members.Length; i++)
                {
                    x.Members[i] = "*****";
                }
            });
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        theSession.SetHeader("color", "blue");

        var streamId =
            theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var streamId2 = theSession.Events.StartStream<Quest>(new QuestStarted{Name = "Find the Eye of the World"}, new MembersJoined(1, "Fal Dara", "Rand", "Perrin", "Mat")).Id;
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking()
            .IncludeEvents(e => e.EventTypesAre(typeof(QuestStarted)))
            .AddHeader("color", "green")
            .AddHeader("opid", 1)
            .ApplyAsync();

        // Should apply to this stream
        var events = await theSession.Events.FetchStreamAsync(streamId);
        var questStarted = events.OfType<IEvent<QuestStarted>>().Single();
        questStarted.Data.Name.ShouldBe("*****");
        questStarted.Headers["color"].ShouldBe("green");
        questStarted.Headers["opid"].ShouldBe(1);

        var joined = events.OfType<Event<MembersJoined>>().Single();
        // Did NOT get masked in this case
        foreach (var memberName in joined.Data.Members)
        {
            memberName.ShouldNotBe("*****");
        }

        // The last event does not get masked
        events.Last().Headers["color"].ShouldBe("blue");

        // Should *not* apply here
        var events2 = await theSession.Events.FetchStreamAsync(streamId2);
        events2.Select(x => x.Data).OfType<QuestStarted>()
            .Single().Name.ShouldBe("*****");
    }

        [Fact]
    public async Task end_to_end_for_string_identified_stream_multiple_rules_and_multiple_streams()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddMaskingRuleForProtectedInformation<QuestStarted>(x => x.Name = "*****");
            opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
            {
                for (int i = 0; i < x.Members.Length; i++)
                {
                    x.Members[i] = "*****";
                }
            });
            opts.Events.MetadataConfig.HeadersEnabled = true;
        });

        theSession.SetHeader("color", "blue");

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<Quest>(streamId, new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        var streamId2 = Guid.NewGuid().ToString();

        theSession.Events.StartStream<Quest>(streamId2, new QuestStarted{Name = "Find the Eye of the World"}, new MembersJoined(1, "Fal Dara", "Rand", "Perrin", "Mat"));
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking()
            .IncludeStream(streamId)
            .IncludeStream(streamId2)
            .AddHeader("color", "green")
            .AddHeader("opid", 1)
            .ApplyAsync();

        // Should apply to this stream
        var events = await theSession.Events.FetchStreamAsync(streamId);
        var questStarted = events.OfType<IEvent<QuestStarted>>().Single();
        questStarted.Data.Name.ShouldBe("*****");
        questStarted.Headers["color"].ShouldBe("green");
        questStarted.Headers["opid"].ShouldBe(1);

        var joined = events.OfType<Event<MembersJoined>>().Single();
        foreach (var memberName in joined.Data.Members)
        {
            memberName.ShouldBe("*****");
        }

        joined.Headers["color"].ShouldBe("green");
        joined.Headers["opid"].ShouldBe(1);

        // The last event does not get masked
        events.Last().Headers["color"].ShouldBe("blue");

        // Should *not* apply here
        var events2 = await theSession.Events.FetchStreamAsync(streamId2);
        events2.Select(x => x.Data).OfType<QuestStarted>()
            .Single().Name.ShouldBe("*****");
    }


}



public interface IAccountEvent
{
    string Name { get; set; }
}

public class AccountChanged: IAccountEvent
{
    public string Name { get; set; }
}


