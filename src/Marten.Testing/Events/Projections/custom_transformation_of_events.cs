using System;
using System.Collections.Generic;
using Marten.Events.Projections;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class custom_transformation_of_events : DocumentSessionFixture<IdentityMap>
    {
        static readonly Guid streamId = Guid.NewGuid();

        QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
        MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
        MonsterSlayed slayed1 = new MonsterSlayed { QuestId = streamId, Name = "Troll" };
        MonsterSlayed slayed2 = new MonsterSlayed { QuestId = streamId, Name = "Dragon" };

        MembersJoined joined2 = new MembersJoined { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };

        [Fact]
        public void from_configuration()
        {
            var events = new List<object>();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.ProjectView<PersistedView>()
                    .ProjectEvent<QuestStarted>((view, @event) => events.Add(@event))
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => events.Add(@event))
                    .ProjectEvent<MonsterSlayed>(e => e.QuestId, (view, @event) => events.Add(@event));
            });

            theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            events.Count.ShouldBe(5);
            events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public async void from_configuration_async()
        {
            var events = new List<object>();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.ProjectView<PersistedView>()
                    .ProjectEvent<QuestStarted>((view, @event) => { events.Add(@event);})
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => { events.Add(@event); })
                    .ProjectEvent<MonsterSlayed>(e => e.QuestId, (view, @event) => { events.Add(@event); });
            });

            theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            events.Count.ShouldBe(5);
            events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public void persist_new_document()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(new PersistDocumentProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public async void persist_new_document_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(new PersistDocumentProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }
    }

    public class PersistedView
    {
        public Guid Id { get; set; }
        public List<object> Events { get; } = new List<object>();
    }

    public class PersistDocumentProjection : ViewProjection<PersistedView>
    {
        public PersistDocumentProjection()
        {
            ProjectEvent<QuestStarted>(Persist);
            ProjectEvent<MembersJoined>(e => e.QuestId, Persist);
            ProjectEvent<MonsterSlayed>(e => e.QuestId, Persist);
        }

        private void Persist<T>(PersistedView view, T @event)
        {
            view.Events.Add(@event);
        }
    }
}