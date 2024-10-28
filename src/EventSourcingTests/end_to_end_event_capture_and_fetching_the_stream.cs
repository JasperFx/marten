using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using EventSourcingTests.Projections;
using EventSourcingTests.Utils;
using JasperFx.CodeGeneration;
using Marten;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class end_to_end_event_capture_and_fetching_the_stream: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;
    private static readonly string[] SameTenants = { "tenant", "tenant" };
    private static readonly string[] DifferentTenants = { "tenant", "differentTenant" };
    private static readonly string[] DefaultTenant = { Tenancy.DefaultTenantId };

    public end_to_end_event_capture_and_fetching_the_stream(ITestOutputHelper output)
    {
        _output = output;
    }

    public static TheoryData<TenancyStyle, string[]> SessionParams = new TheoryData<TenancyStyle, string[]>
    {
        { TenancyStyle.Conjoined, SameTenants },
        { TenancyStyle.Conjoined, DifferentTenants },
        { TenancyStyle.Single, DefaultTenant },
        { TenancyStyle.Single, DifferentTenants },
        { TenancyStyle.Single, SameTenants },
    };

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back(TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        await When.CalledForEachAsync(tenants, async (tenantId, _) =>
        {
            using var session = store.LightweightSession(tenantId);
            session.Logger = new TestOutputMartenLogger(_output);

            #region sample_start-stream-with-aggregate-type

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var id = session.Events.StartStream<Quest>(joined, departed).Id;
            await session.SaveChangesAsync();

            #endregion

            var streamEvents = await session.Events.FetchStreamAsync(id);

            streamEvents.Count().ShouldBe(2);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);

            streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public Task capture_events_to_a_new_stream_and_fetch_the_events_back_async(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        return When.CalledForEachAsync(tenants, async (tenantId, _) =>
        {
            await using var session = store.LightweightSession(tenantId);

            #region sample_start-stream-with-aggregate-type

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var id = session.Events.StartStream<Quest>(joined, departed).Id;
            await session.SaveChangesAsync();

            #endregion

            var streamEvents = await session.Events.FetchStreamAsync(id);

            streamEvents.Count().ShouldBe(2);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);

            streamEvents.Each(e => e.Timestamp.ShouldNotBe(default));
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        return When.CalledForEachAsync(tenants, async (tenantId, _) =>
        {
            await using var session = store.LightweightSession(tenantId);

            #region sample_start-stream-with-aggregate-type

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var id = session.Events.StartStream<Quest>(joined, departed).Id;
            await session.SaveChangesAsync();

            #endregion

            var streamEvents = await session.Events.QueryAllRawEvents()
                .Where(x => x.StreamId == id).OrderBy(x => x.Version).ToListAsync();

            streamEvents.Count().ShouldBe(2);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);

            streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_sync_with_linq(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        await When.CalledForEachAsync(tenants, async (tenantId, _) =>
        {
            using var session = store.LightweightSession(tenantId);

            #region sample_start-stream-with-aggregate-type

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            var id = session.Events.StartStream<Quest>(joined, departed).Id;
            await session.SaveChangesAsync();

            #endregion

            var streamEvents = session.Events.QueryAllRawEvents()
                .Where(x => x.StreamId == id).OrderBy(x => x.Version).ToList();

            streamEvents.Count().ShouldBe(2);
            streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
            streamEvents.ElementAt(0).Version.ShouldBe(1);
            streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
            streamEvents.ElementAt(1).Version.ShouldBe(2);

            streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task live_aggregate_equals_inlined_aggregate_without_hidden_contracts(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);
        var questId = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
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

            using (var session = store.LightweightSession(tenantId))
            {
                var liveAggregate = session.Events.AggregateStream<QuestParty>(questId);
                var inlinedAggregate = session.Load<QuestParty>(questId);
                liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
                inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task open_persisted_stream_in_new_store_with_same_settings(TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);
        var questId = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
                var started = new QuestStarted { Id = questId, Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                await session.SaveChangesAsync();
            }

            // events-aggregate-on-the-fly - works with same store
            using (var session = store.LightweightSession(tenantId))
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestParty>(questId);

                party.Id.ShouldBe(questId);
                party.ShouldNotBeNull();

                var party_at_version_3 = session.Events
                    .AggregateStream<QuestParty>(questId, 3);

                party_at_version_3.ShouldNotBeNull();

                var party_yesterday = session.Events
                    .AggregateStream<QuestParty>(questId, timestamp: DateTimeOffset.UtcNow.AddDays(-1));
                party_yesterday.ShouldBeNull();
            }

            using (var session = store.LightweightSession(tenantId))
            {
                var party = session.Load<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }

            var newStore = InitStore(tenancyStyle, false);

            //Inline is working
            using (var session = store.LightweightSession(tenantId))
            {
                var party = session.Load<QuestParty>(questId);
                party.ShouldNotBeNull();
            }

            //GetAll
            using (var session = store.LightweightSession(tenantId))
            {
                var parties = session.Events.QueryRawEventDataOnly<QuestParty>().ToArray();
                foreach (var party in parties)
                {
                    party.ShouldNotBeNull();
                }
            }

            //This AggregateStream fail with NPE
            using (var session = store.LightweightSession(tenantId))
            {
                // questId is the id of the stream
                var party = session.Events.AggregateStream<QuestParty>(questId); //Here we get NPE
                party.Id.ShouldBe(questId);

                var party_at_version_3 = session.Events
                    .AggregateStream<QuestParty>(questId, 3);
                party_at_version_3.Id.ShouldBe(questId);

                var party_yesterday = session.Events
                    .AggregateStream<QuestParty>(questId, timestamp: DateTimeOffset.UtcNow.AddDays(-1));
                party_yesterday.ShouldBeNull();
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task query_before_saving(TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);
        var questId = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                var parties = session.Query<QuestParty>().ToArray();
                parties.Length.ShouldBeLessThanOrEqualTo(index);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            using (var session = store.LightweightSession(tenantId))
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                await session.SaveChangesAsync();

                var party = session.Events.AggregateStream<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public Task aggregate_stream_async_has_the_id(TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);
        var questId = Guid.NewGuid();

        return When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            await using (var session = store.LightweightSession(tenantId))
            {
                var parties = await session.Query<QuestParty>().ToListAsync();
                parties.Count.ShouldBeLessThanOrEqualTo(index);
            }

            //This SaveChanges will fail with missing method (ro collection configured?)
            await using (var session = store.LightweightSession(tenantId))
            {
                var started = new QuestStarted { Name = "Destroy the One Ring" };
                var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                session.Events.StartStream<Quest>(questId, started, joined1);
                await session.SaveChangesAsync();

                var party = await session.Events.AggregateStreamAsync<QuestParty>(questId);
                party.Id.ShouldBe(questId);
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(
        TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                #region sample_start-stream-with-existing-guid

                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined, departed);
                await session.SaveChangesAsync();

                #endregion

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined);
                session.Events.Append(id, departed);

                await session.SaveChangesAsync();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_an_existing_stream_and_fetch_the_events_back(TenancyStyle tenancyStyle,
        string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        var id = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            var started = new QuestStarted();

            using (var session = store.LightweightSession(tenantId))
            {
                session.Events.StartStream<Quest>(id, started);
                await session.SaveChangesAsync();
            }

            using (var session = store.LightweightSession(tenantId))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(id, joined);
                session.Events.Append(id, departed);

                await session.SaveChangesAsync();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(3);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
                streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(2).Version.ShouldBe(3);
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema(
        TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = session.Events.StartStream<Quest>(joined, departed).Id;
                await session.SaveChangesAsync();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task
        capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided_in_another_database_schema(
            TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined, departed);
                await session.SaveChangesAsync();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);

                streamEvents.Each(x => x.Sequence.ShouldBeGreaterThan(0L));
            }
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(
        TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            using (var session = store.LightweightSession(tenantId))
            {
                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                var id = Guid.NewGuid();
                session.Events.StartStream<Quest>(id, joined);
                session.Events.Append(id, departed);

                await session.SaveChangesAsync();

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(2);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
            }
        }).ShouldSucceedAsync();
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema(
        TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        var id = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            var started = new QuestStarted();

            using (var session = store.LightweightSession(tenantId))
            {
                session.Events.StartStream<Quest>(id, started);
                await session.SaveChangesAsync();
            }

            using (var session = store.LightweightSession(tenantId))
            {
                #region sample_append-events

                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                session.Events.Append(id, joined, departed);

                await session.SaveChangesAsync();

                #endregion

                var streamEvents = session.Events.FetchStream(id);

                streamEvents.Count().ShouldBe(3);
                streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
                streamEvents.ElementAt(0).Version.ShouldBe(1);
                streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
                streamEvents.ElementAt(1).Version.ShouldBe(2);
                streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
                streamEvents.ElementAt(2).Version.ShouldBe(3);
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    [Theory]
    [MemberData(nameof(SessionParams))]
    public async Task assert_on_max_event_id_on_event_stream_append(
        TenancyStyle tenancyStyle, string[] tenants)
    {
        var store = InitStore(tenancyStyle);

        var id = Guid.NewGuid();

        await When.CalledForEachAsync(tenants, async (tenantId, index) =>
        {
            var started = new QuestStarted();

            using (var session = store.LightweightSession(tenantId))
            {
                #region sample_append-events-assert-on-eventid

                session.Events.StartStream<Quest>(id, started);
                await session.SaveChangesAsync();

                var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                var departed = new MembersDeparted { Members = new[] { "Thom" } };

                // Events are appended into the stream only if the maximum event id for the stream
                // would be 3 after the append operation.
                session.Events.Append(id, 3, joined, departed);

                await session.SaveChangesAsync();

                #endregion
            }
        }).ShouldThrowIfAsync(
            (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) ||
            (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
        );
    }

    private DocumentStore InitStore(TenancyStyle tenancyStyle, bool cleanSchema = true,
        bool useAppendEventForUpdateLock = false)
    {
        var store = StoreOptions(_ =>
        {
            _.Events.TenancyStyle = tenancyStyle;

            _.AutoCreateSchemaObjects = AutoCreate.All;

            if (tenancyStyle == TenancyStyle.Conjoined)
                _.Policies.AllDocumentsAreMultiTenanted();

            _.Connection(ConnectionSource.ConnectionString);

            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);

            _.Events.AddEventType(typeof(MembersJoined));
            _.Events.AddEventType(typeof(MembersDeparted));
            _.Events.AddEventType(typeof(QuestStarted));
        }, cleanSchema);


        return store;
    }
}
