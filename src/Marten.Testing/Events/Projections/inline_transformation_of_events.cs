using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class inline_transformation_of_events : DocumentSessionFixture<NulloIdentityMap>
    {
        QuestStarted started = new QuestStarted { Name = "Find the Orb" };
        MembersJoined joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
        MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
        MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

        MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        [Fact]
        public void sync_projection_of_events()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TransformEventsInlineWith(new MonsterDefeatedTransform());
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            var monsterEvents =
                theSession.Events.FetchStream(streamId).OfType<Event<MonsterSlayed>>().ToArray();

            monsterEvents.Length.ShouldBe(2); // precondition

            monsterEvents.Each(e =>
            {
                var doc = theSession.Load<MonsterDefeated>(e.Id);
                doc.Monster.ShouldBe(e.Data.Name);
            });
        }

        [Fact]
        public void sync_projection_of_events_with_direct_configuration()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineTransformation(new OneForOneProjection<MonsterSlayed, MonsterDefeated>(new MonsterDefeatedTransform()));
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            var monsterEvents =
                theSession.Events.FetchStream(streamId).OfType<Event<MonsterSlayed>>().ToArray();

            monsterEvents.Length.ShouldBe(2); // precondition

            monsterEvents.Each(e =>
            {
                var doc = theSession.Load<MonsterDefeated>(e.Id);
                doc.Monster.ShouldBe(e.Data.Name);
            });
        }


        [Fact]
        public async Task async_projection_of_events()
        {
            // SAMPLE: applying-monster-defeated
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);

                _.Events.TransformEventsInlineWith(new MonsterDefeatedTransform());
            });
            // ENDSAMPLE



            // The code below is just customizing the document store
            // used in the tests
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TransformEventsInlineWith(new MonsterDefeatedTransform());
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            var monsterEvents =
                (await theSession.Events.FetchStreamAsync(streamId)).OfType<Event<MonsterSlayed>>().ToArray();

            monsterEvents.Length.ShouldBe(2); // precondition

            foreach (var e in monsterEvents)
            {
                var doc = await theSession.LoadAsync<MonsterDefeated>(e.Id);
                doc.Monster.ShouldBe(e.Data.Name);
            }
        }

    }

    // SAMPLE: MonsterDefeatedTransform
    public class MonsterDefeatedTransform : ITransform<MonsterSlayed, MonsterDefeated>
    {
        public MonsterDefeated Transform(Event<MonsterSlayed> input)
        {
            return new MonsterDefeated
            {
                Id = input.Id,
                Monster = input.Data.Name
            };
        }
    }

    public class MonsterDefeated
    {
        public Guid Id { get; set; }
        public string Monster { get; set; }
    }
    // ENDSAMPLE
}