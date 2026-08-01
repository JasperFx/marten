using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// One string-identified store per EventAppendMode, each in its own schema.
/// Replaces the old Rich-only run of this class on the shared
/// "string_identified_streams" collection plus its Quick-mode whole-file fork in
/// QuickAppend/quick_append_event_capture_and_fetching_the_stream_with_string_identifiers.cs.
/// Fixed stream ids ("First", "Second"...) are safe here because each profile
/// schema is dropped when the fixture builds its store.
/// </summary>
public class StringStreamsByAppendModeFixture: MultiStoreFixture
{
    public static readonly IReadOnlyDictionary<string, EventAppendMode> Cases =
        new Dictionary<string, EventAppendMode>
        {
            { "rich", EventAppendMode.Rich },
            { "quick", EventAppendMode.Quick },
            { "qwst", EventAppendMode.QuickWithServerTimestamps }
        };

    public StringStreamsByAppendModeFixture(): base("esstringstreams")
    {
        foreach (var pair in Cases)
        {
            var mode = pair.Value;
            Profile(pair.Key, opts =>
            {
                opts.Events.AppendMode = mode;
                opts.Events.StreamIdentity = StreamIdentity.AsString;
                opts.Projections.Snapshot<QuestPartyWithStringIdentifier>(SnapshotLifecycle.Inline);

                opts.Events.AddEventType(typeof(MembersJoined));
                opts.Events.AddEventType(typeof(MembersDeparted));
                opts.Events.AddEventType(typeof(QuestStarted));
            });
        }
    }
}

public class end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers
    : IClassFixture<StringStreamsByAppendModeFixture>
{
    private readonly StringStreamsByAppendModeFixture _fixture;

    public end_to_end_event_capture_and_fetching_the_stream_with_string_identifiers(
        StringStreamsByAppendModeFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> Profiles()
    {
        return StringStreamsByAppendModeFixture.Cases.Keys.Select(key => new object[] { key });
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "First";

        session.Events.StartStream<Quest>(id, joined, departed);
        await session.SaveChangesAsync();

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        #region sample_start-stream-with-aggregate-type

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Second";
        session.Events.StartStream<Quest>(id, joined, departed);
        await session.SaveChangesAsync();

        #endregion

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Third";
        session.Events.StartStream<Quest>(id, joined, departed);
        await session.SaveChangesAsync();

        var streamEvents = await session.Events.QueryAllRawEvents()
            .Where(x => x.StreamKey == id).OrderBy(x => x.Version).ToListAsync();

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);

        streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task live_aggregate_equals_inlined_aggregate_without_hidden_contracts(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var questId = "Fifth";

        using (var session = store.LightweightSession())
        {
            //Note Id = questId, is we remove it from first message then AggregateStream will return party.Id=default(Guid) that is not equals to Load<QuestParty> result
            var started = new QuestStarted
            {
                /*Id = questId,*/
                Name = "Destroy the One Ring"
            };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();
        }

        using (var session = store.LightweightSession())
        {
            var liveAggregate = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);
            var inlinedAggregate = await session.LoadAsync<QuestPartyWithStringIdentifier>(questId);
            liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
            inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task open_persisted_stream_in_new_store_with_same_settings(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var questId = "Sixth";

        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(QuestPartyWithStringIdentifier));

        using (var session = store.LightweightSession())
        {
            //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();
        }

        // events-aggregate-on-the-fly - works with same store
        using (var session = store.LightweightSession())
        {
            // questId is the id of the stream
            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);

            party.ShouldNotBeNull();

            var party_at_version_3 = await session.Events
                .AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId, 3);

            party_at_version_3.ShouldBeNull();

            var party_yesterday = await session.Events
                .AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
            party_yesterday.ShouldBeNull();
        }

        using (var session = store.LightweightSession())
        {
            var party = await session.LoadAsync<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }

        var newStore = new DocumentStore(store.Options);

        //Inline is working
        using (var session = newStore.LightweightSession())
        {
            var party = await session.LoadAsync<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }

        //GetAll
        using (var session = store.LightweightSession())
        {
            var parties = (await session.Events.QueryRawEventDataOnly<QuestPartyWithStringIdentifier>().ToListAsync());
            foreach (var party in parties)
            {
                party.ShouldNotBeNull();
            }
        }

        //This AggregateStream fail with NPE
        using (var session = newStore.LightweightSession())
        {
            // questId is the id of the stream
            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId); //Here we get NPE
            party.ShouldNotBeNull();

            var party_yesterday = await session.Events
                .AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId, timestamp: DateTime.UtcNow.AddDays(-1));
            party_yesterday.ShouldBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task query_before_saving(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var questId = "Seventh";

        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(QuestPartyWithStringIdentifier));

        await using (var session = store.LightweightSession())
        {
            var parties = await session.Query<QuestPartyWithStringIdentifier>().CountAsync();
            parties.ShouldBeLessThanOrEqualTo(0);
        }

        //This SaveChanges will fail with missing method (ro collection configured?)
        await using (var session = store.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();

            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task aggregate_stream_async_has_the_id(string profile)
    {
        var store = _fixture.StoreFor(profile);

        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(QuestPartyWithStringIdentifier));

        var questId = "Eighth";

        await using (var session = store.LightweightSession())
        {
            var parties = await session.Query<QuestPartyWithStringIdentifier>().ToListAsync();
            parties.Count.ShouldBeLessThanOrEqualTo(0);
        }

        //This SaveChanges will fail with missing method (ro collection configured?)
        await using (var session = store.LightweightSession())
        {
            var started = new QuestStarted { Name = "Destroy the One Ring" };
            var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

            session.Events.StartStream<Quest>(questId, started, joined1);
            await session.SaveChangesAsync();

            var party = await session.Events.AggregateStreamAsync<QuestPartyWithStringIdentifier>(questId);
            party.ShouldNotBeNull();
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(string profile)
    {
        var store = _fixture.StoreFor(profile);
        using var session = store.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Tenth";
        session.Events.StartStream<Quest>(id, joined, departed);
        await session.SaveChangesAsync();

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_non_existing_stream_and_fetch_the_events_back(string profile)
    {
        var store = _fixture.StoreFor(profile);
        using var session = store.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Eleventh";
        session.Events.StartStream<Quest>(id, joined);
        session.Events.Append(id, departed);

        await session.SaveChangesAsync();

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_an_existing_stream_and_fetch_the_events_back(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var id = "Twelth";
        var started = new QuestStarted();

        using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Quest>(id, started);
            await session.SaveChangesAsync();
        }

        using (var session = store.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(id, joined);
            session.Events.Append(id, departed);

            await session.SaveChangesAsync();

            var streamEvents = await session.Events.FetchStreamAsync(id);

            streamEvents.Count.ShouldBe(3);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);
            streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(2).Version.ShouldBe(3);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema(string profile)
    {
        var store = _fixture.StoreFor(profile);
        using var session = store.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Thirteen";
        session.Events.StartStream<Quest>(id, joined, departed);
        await session.SaveChangesAsync();

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }


    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(string profile)
    {
        var store = _fixture.StoreFor(profile);
        using var session = store.LightweightSession();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = "Fourteen";
        session.Events.StartStream<Quest>(id, joined);
        session.Events.Append(id, departed);

        await session.SaveChangesAsync();

        var streamEvents = await session.Events.FetchStreamAsync(id);

        streamEvents.Count.ShouldBe(2);
        streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
        streamEvents.ElementAt(0).Version.ShouldBe(1);
        streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
        streamEvents.ElementAt(1).Version.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var id = "Fifteen";
        var started = new QuestStarted();

        using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Quest>(id, started);
            await session.SaveChangesAsync();
        }

        using (var session = store.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.Append(id, joined, departed);

            await session.SaveChangesAsync();

            var streamEvents = await session.Events.FetchStreamAsync(id);

            streamEvents.Count.ShouldBe(3);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);
            streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(2).Version.ShouldBe(3);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task assert_on_max_event_id_on_event_stream_append(string profile)
    {
        var store = _fixture.StoreFor(profile);
        var id = "Sixteen";
        var started = new QuestStarted();

        using var session = store.LightweightSession();
        session.Events.StartStream<Quest>(id, started);
        await session.SaveChangesAsync();

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        // Events are appended into the stream only if the maximum event id for the stream
        // would be 3 after the append operation.
        session.Events.Append(id, 3, joined, departed);

        await session.SaveChangesAsync();
    }
}

[CollectionDefinition("string_identified_streams")]
public class StringIdentifiedStreamsCollection: ICollectionFixture<StringIdentifiedStreamsFixture>
{
}

public class StringIdentifiedStreamsFixture: StoreFixture
{
    public StringIdentifiedStreamsFixture(): base("string_identified_streams")
    {
        // ScenarioAggregateAndRepository documents an Append(streamId, version, events)
        // pattern that requires Rich mode (Quick mode requires StartStream first to
        // materialize the stream row).
        Options.Events.AppendMode = EventAppendMode.Rich;
        Options.Events.StreamIdentity = StreamIdentity.AsString;
        Options.Projections.Snapshot<QuestPartyWithStringIdentifier>(SnapshotLifecycle.Inline);

        Options.Events.AddEventType(typeof(MembersJoined));
        Options.Events.AddEventType(typeof(MembersDeparted));
        Options.Events.AddEventType(typeof(QuestStarted));
    }
}

public class QuestPartyWithStringIdentifier
{
    private readonly IList<string> _members = new List<string>();

    public string[] Members
    {
        get
        {
            return _members.ToArray();
        }
        set
        {
            _members.Clear();
            _members.AddRange(value);
        }
    }

    public IList<string> Slayed { get; } = new List<string>();

    public void Apply(MembersJoined joined)
    {
        if (joined.Members != null)
            _members.Fill(joined.Members);
    }

    public void Apply(MembersDeparted departed)
    {
        _members.RemoveAll(x => departed.Members.Contains(x));
    }

    public void Apply(QuestStarted started)
    {
        Name = started.Name;
    }

    public string Key { get; set; }

    public string Name { get; set; }

    public string Id { get; set; }

    public override string ToString()
    {
        return $"Quest party '{Name}' is {Members.Join(", ")}";
    }
}
