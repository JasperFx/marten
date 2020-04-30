using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class event_store_with_string_identifiers_for_stream: IntegrationContext
    {
        public event_store_with_string_identifiers_for_stream(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(storeOptions =>
            {
                // SAMPLE: eventstore-configure-stream-identity
                storeOptions.Events.StreamIdentity = StreamIdentity.AsString;
                storeOptions.Events.AsyncProjections.AggregateStreamsWith<QuestPartyWithStringIdentifier>();
                // ENDSAMPLE
            });
        }

        [Fact]
        public void use_string_id_if_as_string_identifiers()
        {
            var events = new EventGraph(new StoreOptions()) { StreamIdentity = StreamIdentity.AsString };

            var table = new StreamsTable(events);

            table.PrimaryKey.Type.ShouldBe("varchar");
            table.First().Name.ShouldBe("id");
        }

        [Fact]
        public void smoke_test_being_able_to_create_database_objects()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(EventGraph));
        }

        [Fact]
        public void try_to_insert_event_with_string_identifiers()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.Append("First", new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }
        }

        [Fact]
        public void try_to_insert_event_with_string_identifiers_non_typed()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream("First", new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Events.FetchStream("First").Count.ShouldBe(2);
            }
        }

        [Fact]
        public void fetch_state()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.Append("First", new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                var state = session.Events.FetchStreamState("First");
                state.Key.ShouldBe("First");
                state.Version.ShouldBe(2);
            }
        }

        [Fact]
        public async Task fetch_state_async()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.Append("First", new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession())
            {
                var state = await session.Events.FetchStreamStateAsync("First");
                state.Key.ShouldBe("First");
                state.Version.ShouldBe(2);
            }
        }

        [Fact]
        public async Task store_on_multiple_streams_at_a_time()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.Append("First", new MembersJoined(), new MembersJoined());
                session.Events.Append("Second", new MembersJoined(), new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession())
            {
                var state = await session.Events.FetchStreamStateAsync("First");
                state.Key.ShouldBe("First");
                state.Version.ShouldBe(2);

                var state2 = await session.Events.FetchStreamStateAsync("Second");
                state2.Key.ShouldBe("Second");
                state2.Version.ShouldBe(3);
            }
        }

        //[Fact] -- so this does work, but there's a race condition that makes the test unreliable
        public async Task async_daemon_with_string_identifiers()
        {
            using (var session = theStore.OpenSession())
            {
                session.Events.Append("First", new MembersJoined { Members = new string[] { "Bill" } }, new MembersJoined());
                session.Events.Append("Second", new MembersJoined(), new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }

            Thread.Sleep(100);

            using (var daemon = theStore.BuildProjectionDaemon())
            {
                await daemon.RebuildAll();
            }

            Thread.Sleep(50);

            using (var query = theStore.QuerySession())
            {
                SpecificationExtensions.ShouldNotBeNull(query.Load<QuestPartyWithStringIdentifier>("First"));
                SpecificationExtensions.ShouldNotBeNull(query.Load<QuestPartyWithStringIdentifier>("Second"));
            }
        }
    }
}
