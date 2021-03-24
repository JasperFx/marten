using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class fetch_a_single_event_with_metadata: IntegrationContext
    {
        private QuestStarted started = new QuestStarted { Name = "Find the Orb" };
        private MembersJoined joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
        private MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
        private MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

        private MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        [Fact]
        public void fetch_synchronously()
        {
            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
            theSession.SaveChanges();

            var events = theSession.Events.FetchStream(streamId);

            SpecificationExtensions.ShouldBeNull(theSession.Events.Load(Guid.NewGuid()));

            // Knowing the event type
            var slayed1_2 = theSession.Events.Load<MonsterSlayed>(events[2].Id);
            slayed1_2.Version.ShouldBe(3);
            slayed1_2.Data.Name.ShouldBe("Troll");

            // Not knowing the event type
            var slayed1_3 = theSession.Events.Load<MonsterSlayed>(events[2].Id).ShouldBeOfType<Event<MonsterSlayed>>();
            slayed1_3.Version.ShouldBe(3);
            slayed1_3.Data.Name.ShouldBe("Troll");
        }

        [Fact]
        public async Task fetch_asynchronously()
        {
            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);

            SpecificationExtensions.ShouldBeNull((await theSession.Events.LoadAsync(Guid.NewGuid())));
            SpecificationExtensions.ShouldBeNull((await theSession.Events.LoadAsync<MonsterSlayed>(Guid.NewGuid())));

            // Knowing the event type
            var slayed1_2 = await theSession.Events.LoadAsync<MonsterSlayed>(events[2].Id);
            slayed1_2.Version.ShouldBe(3);
            slayed1_2.Data.Name.ShouldBe("Troll");

            // Not knowing the event type
            var slayed1_3 = (await theSession.Events.LoadAsync<MonsterSlayed>(events[2].Id)).ShouldBeOfType<Event<MonsterSlayed>>();
            slayed1_3.Version.ShouldBe(3);
            slayed1_3.Data.Name.ShouldBe("Troll");
        }

        [Fact]
        public async Task fetch_in_batch_query()
        {
            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);

            var batch = theSession.CreateBatchQuery();

            var slayed1_2 = batch.Events.Load(events[2].Id);
            var slayed2_2 = batch.Events.Load(events[3].Id);
            var missing = batch.Events.Load(Guid.NewGuid());

            await batch.Execute();

            (await slayed1_2).ShouldBeOfType<Event<MonsterSlayed>>()
                .Data.Name.ShouldBe("Troll");

            (await slayed2_2).ShouldBeOfType<Event<MonsterSlayed>>()
                .Data.Name.ShouldBe("Dragon");

            SpecificationExtensions.ShouldBeNull((await missing));
        }

        public fetch_a_single_event_with_metadata(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
