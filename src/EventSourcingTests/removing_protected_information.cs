using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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

    private record AccountChangedRecord(string FirstName, string LastName);

    [Fact]
    public void match_exactly_on_event_type_when_record()
    {
        theEvents.AddMaskingRuleForProtectedInformation<AccountChangedRecord>(x => x with { LastName = "****" });

        var started = new AccountChangedRecord("John", "Doe");

        var @event = new Event<AccountChangedRecord>(started);

        theEvents.TryMask(@event).ShouldBeTrue();

        @event.Data.FirstName.ShouldBe("John");
        @event.Data.LastName.ShouldBe("****");
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

        await theStore.Advanced.ApplyEventDataMasking(x =>
        {
            x.IncludeStream(streamId)
                .AddHeader("color", "green")
                .AddHeader("opid", 1);
        });

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

        await theStore.Advanced.ApplyEventDataMasking(x =>
            {
                x.IncludeStream(streamId)
                    .AddHeader("color", "green")
                    .AddHeader("opid", 1);
            });

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

        await theStore.Advanced.ApplyEventDataMasking(x =>
        {
            x
                .IncludeStream(streamId)
                .IncludeStream(streamId2)
                .AddHeader("color", "green")
                .AddHeader("opid", 1);
        });

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

        await theStore.Advanced.ApplyEventDataMasking(x =>
            {
                x.IncludeEvents(e => e.EventTypesAre(typeof(QuestStarted)))
                    .AddHeader("color", "green")
                    .AddHeader("opid", 1);
            });

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

        await theStore.Advanced.ApplyEventDataMasking(x =>
            {
                x.IncludeStream(streamId)
                    .IncludeStream(streamId2)
                    .AddHeader("color", "green")
                    .AddHeader("opid", 1);
            });

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
    public async Task end_to_end_masking_by_guid_identified_stream_and_filter_within_stream()
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

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<Quest>(streamId, new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam"), new MembersJoined(3, "Brandybuck", "Merry", "Pippin"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking(x =>
        {
            x
                .IncludeStream(streamId, e => e.Data is MembersJoined { Location: "Hobbiton" })
                .AddHeader("color", "green")
                .AddHeader("opid", 1);
        });

        var events = await theSession.Events.FetchStreamAsync(streamId);

        // Should have matched and been masked
        var hobbiton = events.OfType<Event<MembersJoined>>().Single(x => x.Data.Location == "Hobbiton");
        hobbiton.Headers["color"].ShouldBe("green");
        hobbiton.Data.Members.All(x => x == "*****").ShouldBeTrue();


        // Should NOT have been matched or masked
        var brandybuck = events.OfType<Event<MembersJoined>>().Single(x => x.Data.Location == "Brandybuck");
        brandybuck.Headers["color"].ShouldBe("blue");
        brandybuck.Data.Members.All(x => x != "*****").ShouldBeTrue();
    }

        [Fact]
    public async Task end_to_end_masking_by_string_identified_stream_and_filter_within_stream()
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
        theSession.Events.StartStream<Quest>(streamId, new QuestStarted{Name = "Find Gandalf"}, new MembersJoined(1, "Hobbiton", "Frodo", "Sam"), new MembersJoined(3, "Brandybuck", "Merry", "Pippin"));
        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new MembersDeparted { Members = new string[] { "Frodo" } });
        await theSession.SaveChangesAsync();

        await theStore.Advanced.ApplyEventDataMasking(x =>
        {
            x
                .IncludeStream(streamId, e => e.Data is MembersJoined { Location: "Hobbiton" })
                .AddHeader("color", "green")
                .AddHeader("opid", 1);
        });

        var events = await theSession.Events.FetchStreamAsync(streamId);

        // Should have matched and been masked
        var hobbiton = events.OfType<Event<MembersJoined>>().Single(x => x.Data.Location == "Hobbiton");
        hobbiton.Headers["color"].ShouldBe("green");
        hobbiton.Data.Members.All(x => x == "*****").ShouldBeTrue();


        // Should NOT have been matched or masked
        var brandybuck = events.OfType<Event<MembersJoined>>().Single(x => x.Data.Location == "Brandybuck");
        brandybuck.Headers["color"].ShouldBe("blue");
        brandybuck.Data.Members.All(x => x != "*****").ShouldBeTrue();
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

public static class DocumentationSamples
{
    public static void define_masking_rules()
    {
        #region sample_defining_masking_rules

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // By a single, concrete type
            opts.Events.AddMaskingRuleForProtectedInformation<AccountChanged>(x =>
            {
                // I'm only masking a single property here, but you could do as much as you want
                x.Name = "****";
            });

            // Maybe you have an interface that multiple event types implement that would help
            // make these rules easier by applying to any event type that implements this interface
            opts.Events.AddMaskingRuleForProtectedInformation<IAccountEvent>(x => x.Name = "****");

            // Little fancier
            opts.Events.AddMaskingRuleForProtectedInformation<MembersJoined>(x =>
            {
                for (int i = 0; i < x.Members.Length; i++)
                {
                    x.Members[i] = "*****";
                }
            });
        });

        #endregion
    }

    #region sample_apply_masking_to_a_single_stream

    public static Task apply_masking_to_streams(IDocumentStore store, Guid streamId, CancellationToken token)
    {
        return store
            .Advanced
            .ApplyEventDataMasking(x =>
            {
                x.IncludeStream(streamId);

                // You can add or modify event metadata headers as well
                // BUT, you'll of course need event header tracking to be enabled
                x.AddHeader("masked", DateTimeOffset.UtcNow);
            }, token);
    }

    #endregion

    #region sample_apply_masking_to_a_single_stream_and_filter

    public static Task apply_masking_to_streams_and_filter(IDocumentStore store, Guid streamId, CancellationToken token)
    {
        return store
            .Advanced
            .ApplyEventDataMasking(x =>
            {
                // Mask selected events within a single stream by a user defined criteria
                x.IncludeStream(streamId, e => e.EventTypesAre(typeof(MembersJoined), typeof(MembersDeparted)));

                // You can add or modify event metadata headers as well
                // BUT, you'll of course need event header tracking to be enabled
                x.AddHeader("masked", DateTimeOffset.UtcNow);
            }, token);
    }

    #endregion


    #region sample_apply_masking_by_filter

    public static Task apply_masking_by_filter(IDocumentStore store, Guid[] streamIds)
    {
        return store.Advanced.ApplyEventDataMasking(x =>
            {
                x.IncludeEvents(e => e.EventTypesAre(typeof(QuestStarted)) && e.StreamId.IsOneOf(streamIds));
            });
    }

    #endregion

    #region sample_apply_masking_with_multi_tenancy

    public static Task apply_masking_by_tenant(IDocumentStore store, string tenantId, Guid streamId)
    {
        return store
            .Advanced
            .ApplyEventDataMasking(x =>
            {
                x.IncludeStream(streamId);

                // Specify the tenant id, and it doesn't matter
                // in what order this appears in
                x.ForTenant(tenantId);
            });
    }

    #endregion
}


