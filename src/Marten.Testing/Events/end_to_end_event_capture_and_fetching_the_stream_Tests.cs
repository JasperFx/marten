using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Storage;
using Marten.Testing.Events.Projections;
using Marten.Testing.Events.Utils;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events
{
    [Collection("projections")]
    public class end_to_end_event_capture_and_fetching_the_stream_Tests : OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;
        private static readonly string[] SameTenants = { "tenant", "tenant" };
        private static readonly string[] DiffetentTenants = { "tenant", "differentTenant" };
        private static readonly string[] DefaultTenant = { Tenancy.DefaultTenantId };

        public end_to_end_event_capture_and_fetching_the_stream_Tests(ITestOutputHelper output) : base("projections")
        {
            _output = output;
        }

        public static TheoryData<DocumentTracking, TenancyStyle, string[]> SessionParams = new TheoryData<DocumentTracking, TenancyStyle, string[]>
        {
            { DocumentTracking.IdentityOnly, TenancyStyle.Conjoined, SameTenants },
            { DocumentTracking.DirtyTracking, TenancyStyle.Conjoined, DiffetentTenants },

            { DocumentTracking.IdentityOnly, TenancyStyle.Conjoined, SameTenants },
            { DocumentTracking.DirtyTracking, TenancyStyle.Conjoined, DiffetentTenants },

            { DocumentTracking.IdentityOnly, TenancyStyle.Single, DefaultTenant },
            { DocumentTracking.DirtyTracking, TenancyStyle.Single, DefaultTenant },

            { DocumentTracking.IdentityOnly, TenancyStyle.Single, DiffetentTenants },
            { DocumentTracking.DirtyTracking, TenancyStyle.Single, DiffetentTenants },

            { DocumentTracking.IdentityOnly, TenancyStyle.Single, SameTenants },
            { DocumentTracking.DirtyTracking, TenancyStyle.Single, SameTenants },
        };

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    session.Logger = new TestOutputMartenLogger(_output);

                    #region sample_start-stream-with-aggregate-type
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = session.Events.StartStream<Quest>(joined, departed).Id;
                    session.SaveChanges();
                    #endregion

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);

                    streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public Task capture_events_to_a_new_stream_and_fetch_the_events_back_async(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            return When.CalledForEachAsync(tenants, async (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
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
                }
            }).ShouldSucceedAsync();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public Task capture_events_to_a_new_stream_and_fetch_the_events_back_async_with_linq(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            return When.CalledForEachAsync(tenants, async (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
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
                }
            }).ShouldSucceedAsync();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_sync_with_linq(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    #region sample_start-stream-with-aggregate-type
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = session.Events.StartStream<Quest>(joined, departed).Id;
                    session.SaveChanges();
                    #endregion

                    var streamEvents = session.Events.QueryAllRawEvents()
                        .Where(x => x.StreamId == id).OrderBy(x => x.Version).ToList();

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);

                    streamEvents.Each(e => e.Timestamp.ShouldNotBe(default(DateTimeOffset)));
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void live_aggregate_equals_inlined_aggregate_without_hidden_contracts(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);
            var questId = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    //Note Id = questId, is we remove it from first message then AggregateStream will return party.Id=default(Guid) that is not equals to Load<QuestParty> result
                    var started = new QuestStarted { /*Id = questId,*/ Name = "Destroy the One Ring" };
                    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                    session.Events.StartStream<Quest>(questId, started, joined1);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var liveAggregate = session.Events.AggregateStream<QuestParty>(questId);
                    var inlinedAggregate = session.Load<QuestParty>(questId);
                    liveAggregate.Id.ShouldBe(inlinedAggregate.Id);
                    inlinedAggregate.ToString().ShouldBe(liveAggregate.ToString());
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void open_persisted_stream_in_new_store_with_same_settings(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);
            var questId = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    //Note "Id = questId" @see live_aggregate_equals_inlined_aggregate...
                    var started = new QuestStarted { Id = questId, Name = "Destroy the One Ring" };
                    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                    session.Events.StartStream<Quest>(questId, started, joined1);
                    session.SaveChanges();
                }

                // events-aggregate-on-the-fly - works with same store
                using (var session = store.OpenSession(tenantId, sessionType))
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

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var party = session.Load<QuestParty>(questId);
                    party.Id.ShouldBe(questId);
                }

                var newStore = InitStore(tenancyStyle, false);

                //Inline is working
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var party = session.Load<QuestParty>(questId);
                    SpecificationExtensions.ShouldNotBeNull(party);
                }
                //GetAll
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var parties = session.Events.QueryRawEventDataOnly<QuestParty>().ToArray();
                    foreach (var party in parties)
                    {
                        SpecificationExtensions.ShouldNotBeNull(party);
                    }
                }
                //This AggregateStream fail with NPE
                using (var session = newStore.OpenSession(tenantId, sessionType))
                {
                    // questId is the id of the stream
                    var party = session.Events.AggregateStream<QuestParty>(questId);//Here we get NPE
                    party.Id.ShouldBe(questId);

                    var party_at_version_3 = session.Events
                        .AggregateStream<QuestParty>(questId, 3);
                    party_at_version_3.Id.ShouldBe(questId);

                    var party_yesterday = session.Events
                        .AggregateStream<QuestParty>(questId, timestamp: DateTimeOffset.UtcNow.AddDays(-1));
                    party_yesterday.ShouldBeNull();
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void query_before_saving(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);
            var questId = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var parties = session.Query<QuestParty>().ToArray();
                    parties.Length.ShouldBeLessThanOrEqualTo(index);
                }

                //This SaveChanges will fail with missing method (ro collection configured?)
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var started = new QuestStarted { Name = "Destroy the One Ring" };
                    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                    session.Events.StartStream<Quest>(questId, started, joined1);
                    session.SaveChanges();

                    var party = session.Events.AggregateStream<QuestParty>(questId);
                    party.Id.ShouldBe(questId);
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public Task aggregate_stream_async_has_the_id(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);
            var questId = Guid.NewGuid();

            return When.CalledForEachAsync(tenants, async (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var parties = await session.Query<QuestParty>().ToListAsync();
                    parties.Count.ShouldBeLessThanOrEqualTo(index);
                }

                //This SaveChanges will fail with missing method (ro collection configured?)
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var started = new QuestStarted { Name = "Destroy the One Ring" };
                    var joined1 = new MembersJoined(1, "Hobbiton", "Frodo", "Merry");

                    session.Events.StartStream<Quest>(questId, started, joined1);
                    await session.SaveChangesAsync();

                    var party = await session.Events.AggregateStreamAsync<QuestParty>(questId);
                    party.Id.ShouldBe(questId);
                }
            }).ShouldThrowIfAsync(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    #region sample_start-stream-with-existing-guid
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = Guid.NewGuid();
                    session.Events.StartStream<Quest>(id, joined, departed);
                    session.SaveChanges();
                    #endregion

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = Guid.NewGuid();
                    session.Events.StartStream<Quest>(id, joined);
                    session.Events.Append(id, departed);

                    session.SaveChanges();

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            var id = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                var started = new QuestStarted();

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    session.Events.StartStream<Quest>(id, started);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    session.Events.Append(id, joined);
                    session.Events.Append(id, departed);

                    session.SaveChanges();

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(3);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<QuestStarted>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);
                    streamEvents.ElementAt(2).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(2).Version.ShouldBe(3);
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_new_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = session.Events.StartStream<Quest>(joined, departed).Id;
                    session.SaveChanges();

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void
            capture_events_to_a_new_stream_and_fetch_the_events_back_with_stream_id_provided_in_another_database_schema(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = Guid.NewGuid();
                    session.Events.StartStream<Quest>(id, joined, departed);
                    session.SaveChanges();

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);

                    streamEvents.Each(x => SpecificationExtensions.ShouldBeGreaterThan(x.Sequence, 0L));
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_a_non_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    var id = Guid.NewGuid();
                    session.Events.StartStream<Quest>(id, joined);
                    session.Events.Append(id, departed);

                    session.SaveChanges();

                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count().ShouldBe(2);
                    streamEvents.ElementAt(0).Data.ShouldBeOfType<MembersJoined>();
                    streamEvents.ElementAt(0).Version.ShouldBe(1);
                    streamEvents.ElementAt(1).Data.ShouldBeOfType<MembersDeparted>();
                    streamEvents.ElementAt(1).Version.ShouldBe(2);
                }
            }).ShouldSucceed();
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_events_to_an_existing_stream_and_fetch_the_events_back_in_another_database_schema(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            var id = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                var started = new QuestStarted();

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    session.Events.StartStream<Quest>(id, started);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    #region sample_append-events
                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    session.Events.Append(id, joined, departed);

                    session.SaveChanges();
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
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void assert_on_max_event_id_on_event_stream_append(
            DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            var id = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                var started = new QuestStarted();

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    #region sample_append-events-assert-on-eventid
                    session.Events.StartStream<Quest>(id, started);
                    session.SaveChanges();

                    var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
                    var departed = new MembersDeparted { Members = new[] { "Thom" } };

                    // Events are appended into the stream only if the maximum event id for the stream
                    // would be 3 after the append operation.
                    session.Events.Append(id, 3, joined, departed);

                    session.SaveChanges();
                    #endregion
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        [Theory]
        [MemberData(nameof(SessionParams))]
        public void capture_immutable_events(DocumentTracking sessionType, TenancyStyle tenancyStyle, string[] tenants)
        {
            var store = InitStore(tenancyStyle);

            var id = Guid.NewGuid();

            When.CalledForEach(tenants, (tenantId, index) =>
            {
                var immutableEvent = new ImmutableEvent(id, "some-name");

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    session.Events.Append(id, immutableEvent);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession(tenantId, sessionType))
                {
                    var streamEvents = session.Events.FetchStream(id);

                    streamEvents.Count.ShouldBe(1);
                    var @event = streamEvents.ElementAt(0).Data.ShouldBeOfType<ImmutableEvent>();

                    @event.Id.ShouldBe(id);
                    @event.Name.ShouldBe("some-name");
                }
            }).ShouldThrowIf(
                (tenancyStyle == TenancyStyle.Single && tenants.Length > 1) || (tenancyStyle == TenancyStyle.Conjoined && tenants.SequenceEqual(SameTenants))
            );
        }

        private DocumentStore InitStore(TenancyStyle tenancyStyle, bool cleanSchema = true, bool useAppendEventForUpdateLock = false)
        {
            var databaseSchema = $"end_to_end_event_capture_{tenancyStyle.ToString().ToLower()}";

            var store = StoreOptions(_ =>
            {
                _.Events.DatabaseSchemaName = databaseSchema;
                _.Events.TenancyStyle = tenancyStyle;

                _.AutoCreateSchemaObjects = AutoCreate.All;

                if (tenancyStyle == TenancyStyle.Conjoined)
                    _.Policies.AllDocumentsAreMultiTenanted();

                _.Connection(ConnectionSource.ConnectionString);

                _.Projections.SelfAggregate<QuestParty>();

                _.Events.AddEventType(typeof(MembersJoined));
                _.Events.AddEventType(typeof(MembersDeparted));
                _.Events.AddEventType(typeof(QuestStarted));
            }, cleanSchema);


            return store;
        }
    }
}
