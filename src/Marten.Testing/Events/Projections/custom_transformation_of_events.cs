using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class custom_transformation_of_events : DocumentSessionFixture<IdentityMap>
    {
        QuestStarted started = new QuestStarted { Name = "Find the Orb" };
        MembersJoined joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
        MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
        MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

        MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        [Fact]
        public void from_configuration()
        {
            var events = new List<object>();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.Project<PersistedProjection>()
                    .Event<QuestStarted>((session, streamId, @event) => events.Add(@event))
                    .Event<MembersJoined>((session, streamId, @event) => events.Add(@event))
                    .Event<MonsterSlayed>((session, streamId, @event) => events.Add(@event));
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
                _.Events.Project<PersistedProjection>()
                    .EventAsync<QuestStarted>((session, streamId, @event) => { events.Add(@event); return Task.FromResult(0); })
                    .EventAsync<MembersJoined>((session, streamId, @event) => { events.Add(@event); return Task.FromResult(0); })
                    .EventAsync<MonsterSlayed>((session, streamId, @event) => { events.Add(@event); return Task.FromResult(0); });
            });

            theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            events.Count.ShouldBe(5);
            events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public void from_custom_class()
        {
            var projection = new MyEventProjection();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(projection);
            });

            theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            projection.Events.Count.ShouldBe(5);
            projection.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public async void from_custom_class_async()
        {
            var projection = new MyAsyncEventProjection();

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(projection);
            });

            theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            projection.Events.Count.ShouldBe(5);
            projection.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public void persist_new_document()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(new PersistDocumentProjection());
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedProjection>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }

        [Fact]
        public async void persist_new_document_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.Add(new PersistDocumentAsyncProjection());
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<PersistedProjection>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);
        }
    }

    public class PersistedProjection
    {
        public Guid Id { get; set; }
        public List<object> Events { get; } = new List<object>();
    }

    public class PersistDocumentProjection : EventProjection<PersistedProjection>
    {
        public PersistDocumentProjection()
        {
            Event<QuestStarted>(Persist);
            Event<MembersJoined>(Persist);
            Event<MonsterSlayed>(Persist);
        }

        private void Persist<T>(IDocumentSession session, Guid streamId, T @event)
        {
            var document = GetDocument(session, streamId);
            document.Events.Add(@event);
        }

        private static PersistedProjection GetDocument(IDocumentSession session, Guid id)
        {
            var document = session.Load<PersistedProjection>(id) ?? new PersistedProjection { Id = id };
            session.Store(document);
            return document;
        }
    }

    public class PersistDocumentAsyncProjection : EventProjection<PersistedProjection>
    {
        public PersistDocumentAsyncProjection()
        {
            EventAsync<QuestStarted>(PersistAsync);
            EventAsync<MembersJoined>(PersistAsync);
            EventAsync<MonsterSlayed>(PersistAsync);
        }

        private async Task PersistAsync<T>(IDocumentSession session, Guid streamId, T @event)
        {
            var document = await GetDocumentAsync(session, streamId);
            document.Events.Add(@event);
        }

        private static async Task<PersistedProjection> GetDocumentAsync(IDocumentSession session, Guid id)
        {
            var document = await session.LoadAsync<PersistedProjection>(id) ?? new PersistedProjection { Id = id };
            session.Store(document);
            return document;
        }
    }

    public class MyEventProjection : EventProjection<PersistedProjection>
    {
        public IList<object> Events { get; } = new List<object>();

        public MyEventProjection()
        {
            Event<QuestStarted>(AddEvent);
            Event<MembersJoined>(AddEvent);
            Event<MonsterSlayed>(AddEvent);
        }

        private void AddEvent<T>(IDocumentSession session, Guid streamId, T @event)
        {
            Events.Add(@event);
        }
    }

    public class MyAsyncEventProjection : EventProjection<PersistedProjection>
    {
        public IList<object> Events { get; } = new List<object>();

        public MyAsyncEventProjection()
        {
            EventAsync<QuestStarted>(AddEventAsync);
            EventAsync<MembersJoined>(AddEventAsync);
            EventAsync<MonsterSlayed>(AddEventAsync);
        }

        private Task<int> AddEventAsync<T>(IDocumentSession session, Guid streamId, T @event)
        {
            Events.Add(@event);
            return Task.FromResult(0);
        }
    }
}