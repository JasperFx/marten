using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class multi_tenancy_and_event_capture: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public static TheoryData<TenancyStyle> TenancyStyles = new TheoryData<TenancyStyle>
    {
        { TenancyStyle.Conjoined },
        { TenancyStyle.Single },
    };

    [Fact]
    public async Task capture_events_for_multiple_tenants_in_one_session_as_string_identified()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;

        }, true);

        theSession.Logger = new TestOutputMartenLogger(_output);
        theSession.ForTenant("one").Events.StartStream("s1", new AEvent(), new BEvent());
        theSession.ForTenant("two").Events.StartStream("s1", new CEvent(), new DEvent(), new QuestStarted());

        await theSession.SaveChangesAsync();

        await using var queryOne = theStore.QuerySession("one");
        var eventsOne = await queryOne.Events.FetchStreamAsync("s1");
        eventsOne[0].Data.ShouldBeOfType<AEvent>();
        eventsOne[1].Data.ShouldBeOfType<BEvent>();


        await using var queryTwo = theStore.QuerySession("two");
        var eventsTwo = await queryTwo.Events.FetchStreamAsync("s1");

        eventsTwo.Count.ShouldBe(3);
    }

    [Fact]
    public async Task delete_all_tenant_data_catches_event_data()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;

        }, true);

        theSession.Logger = new TestOutputMartenLogger(_output);
        theSession.ForTenant("one").Events.StartStream("s1", new AEvent(), new BEvent());
        theSession.ForTenant("two").Events.StartStream("s1", new CEvent(), new DEvent(), new QuestStarted());

        await theSession.SaveChangesAsync();

        await theStore.Advanced.DeleteAllTenantDataAsync("one", CancellationToken.None);

        using var queryOne = theStore.QuerySession("one");
        (await queryOne.Events.QueryAllRawEvents().AnyAsync()).ShouldBeFalse();

        await using var queryTwo = theStore.QuerySession("two");
        var eventsTwo = await queryTwo.Events.FetchStreamAsync("s1");

        eventsTwo.Count.ShouldBe(3);
    }

    [Fact]
    public async Task capture_events_for_multiple_tenants_in_one_session_as_guid_identified()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

        }, true);

        var streamId = Guid.NewGuid();

        theSession.Logger = new TestOutputMartenLogger(_output);

        theSession.ForTenant("one").Events.StartStream(streamId, new AEvent(), new BEvent());
        theSession.ForTenant("two").Events.StartStream(streamId, new CEvent(), new DEvent(), new QuestStarted());

        await theSession.SaveChangesAsync();

        await using var queryOne = theStore.QuerySession("one");
        var eventsOne = await queryOne.Events.FetchStreamAsync(streamId);
        eventsOne[0].Data.ShouldBeOfType<AEvent>();
        eventsOne[1].Data.ShouldBeOfType<BEvent>();


        await using var queryTwo = theStore.QuerySession("two");
        var eventsTwo = await queryTwo.Events.FetchStreamAsync(streamId);

        eventsTwo.Count.ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(TenancyStyles))]
    public async Task capture_events_for_a_tenant(TenancyStyle tenancyStyle)
    {
        InitStore(tenancyStyle);

        Guid stream = Guid.NewGuid();
        using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Green"))
        {
            var events = await session.Events.FetchStreamAsync(stream);
            foreach (var @event in events)
            {
                @event.TenantId.ShouldBe("Green");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TenancyStyles))]
    public async Task capture_events_for_a_tenant_async(TenancyStyle tenancyStyle)
    {
        InitStore(tenancyStyle);

        Guid stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Green"))
        {
            var events = await session.Events.FetchStreamAsync(stream);
            foreach (var @event in events)
            {
                @event.TenantId.ShouldBe("Green");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TenancyStyles))]
    public async Task capture_events_for_a_tenant_with_string_identifier(TenancyStyle tenancyStyle)
    {
        InitStore(tenancyStyle, StreamIdentity.AsString);

        var stream = "SomeStream";
        using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Green"))
        {
            var events = await session.Events.FetchStreamAsync(stream);
            foreach (var @event in events)
            {
                @event.TenantId.ShouldBe("Green");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TenancyStyles))]
    public async Task capture_events_for_a_tenant_async_as_string_identifier(TenancyStyle tenancyStyle)
    {
        InitStore(tenancyStyle, StreamIdentity.AsString);

        var stream = "SomeStream";
        await using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("Green"))
        {
            var events = await session.Events.FetchStreamAsync(stream);
            foreach (var @event in events)
            {
                @event.TenantId.ShouldBe("Green");
            }
        }
    }

    [Theory]
    [MemberData(nameof(TenancyStyles))]
    public async Task append_to_events_a_second_time_with_same_tenant_id(TenancyStyle tenancyStyle)
    {
        InitStore(tenancyStyle);

        Guid stream = Guid.NewGuid();
        using (var session = theStore.LightweightSession("Green"))
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Green"))
        {
            session.Logger = new TestOutputMartenLogger(_output);
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Green"))
        {
            var events = await session.Events.FetchStreamAsync(stream);
            foreach (var @event in events)
            {
                @event.TenantId.ShouldBe("Green");
            }
        }
    }


    [Fact]
    public async Task try_to_append_across_tenants_with_tenancy_style_conjoined()
    {
        InitStore(TenancyStyle.Conjoined);

        Guid stream = Guid.NewGuid();
        using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(stream, new MembersJoined(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        await Should.NotThrowAsync(async () =>
        {
            using (var session = theStore.LightweightSession("Red"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }
        });
    }

    [Fact]
    public async Task tenanted_session_should_not_see_other_tenants_events()
    {
        InitStore(TenancyStyle.Conjoined);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        using (var session = theStore.LightweightSession("Green"))
        {
            session.Events.Append(Guid.NewGuid(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Red"))
        {
            session.Events.Append(Guid.NewGuid(), new MembersJoined());
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession("Green"))
        {
            var memberJoins = session.Query<MembersJoined>().ToList();
            memberJoins.Count.ShouldBe(1);
        }
    }

    private void InitStore(TenancyStyle tenancyStyle, StreamIdentity streamIdentity = StreamIdentity.AsGuid)
    {
        StoreOptions(_ =>
        {
            _.Events.TenancyStyle = tenancyStyle;
            _.Events.StreamIdentity = streamIdentity;
            _.Policies.AllDocumentsAreMultiTenanted();
        }, true);
    }

    public multi_tenancy_and_event_capture(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<StreamIdentity, Func<DocumentStore, IDocumentSession>, Action<IDocumentSession>, Action<IDocumentSession>> WillParameterizeTenantId => new()
    {
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession(),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.Append(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); }
        },
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.Append(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); }
        },
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession(),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.AppendOptimistic(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.AppendOptimistic(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession(),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.AppendExclusive(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsGuid,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
            s => { s.Events.AppendExclusive(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession(),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.Append("Stream", new MembersJoined()); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.Append("Stream", new MembersJoined()); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession(),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.AppendOptimistic("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.AppendOptimistic("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession(),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.AppendExclusive("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
        },
        {
            StreamIdentity.AsString,
            s => s.LightweightSession("Green"),
            s => { s.Events.StartStream("Stream", new MembersJoined()); },
            s => { s.Events.AppendExclusive("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
        },
    };

    [Theory]
    [MemberData(nameof(WillParameterizeTenantId))]
    public async Task will_parameterize_tenant_id_when_checking_stream_version(StreamIdentity streamIdentity, Func<DocumentStore, IDocumentSession> LightweightSession, Action<IDocumentSession> startStream, Action<IDocumentSession> append)
    {
        InitStore(TenancyStyle.Conjoined, streamIdentity);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamId = Guid.NewGuid();
        using (var session = LightweightSession(theStore))
        {
            startStream(session);
            await session.SaveChangesAsync();
        }

        using (var session = LightweightSession(theStore))
        {
            append(session);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Red"))
        {
            startStream(session);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession("Red"))
        {
            append(session);
            await session.SaveChangesAsync();
        }
    }
}
