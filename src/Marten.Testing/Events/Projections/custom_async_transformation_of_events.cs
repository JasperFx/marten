using System;
using System.Collections.Generic;
using Marten.Events.Projections;
using Marten.Services;
using Shouldly;
using Xunit;
using System.Threading.Tasks;

namespace Marten.Testing.Events.Projections
{
    public class project_events_async_from_multiple_streams_into_view : DocumentSessionFixture<IdentityMap>
    {
        static readonly Guid streamId = Guid.NewGuid();
        static readonly Guid streamId2 = Guid.NewGuid();

        QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
        QuestStarted started2 = new QuestStarted { Id = streamId2, Name = "Find the Orb 2.0" };
        MonsterQuestsAdded monsterQuestsAdded = new MonsterQuestsAdded { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
        MonsterQuestsRemoved monsterQuestsRemoved = new MonsterQuestsRemoved { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
        QuestEnded ended = new QuestEnded { Id = streamId, Name = "Find the Orb" };
        MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
        MonsterSlayed slayed1 = new MonsterSlayed { QuestId = streamId, Name = "Troll" };
        MonsterSlayed slayed2 = new MonsterSlayed { QuestId = streamId, Name = "Dragon" };
        MonsterDestroyed destroyed = new MonsterDestroyed { QuestId = streamId, Name = "Troll" };
        MembersDeparted departed = new MembersDeparted { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };
        MembersJoined joined2 = new MembersJoined { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };

        [Fact]
        public void from_configuration()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .ProjectEventAsync<MembersJoined>(e => e.QuestId, (view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .ProjectEventAsync<MonsterSlayed>(e => e.QuestId, (view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .DeleteEvent<QuestEnded>()
                    .DeleteEvent<MembersDeparted>(e => e.QuestId)
                    .DeleteEvent<MonsterDestroyed>((session, e) => session.Load<QuestParty>(e.QuestId).Id);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            theSession.SaveChanges();

            theSession.Events.Append(streamId, joined2);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }

        [Fact]
        public async void from_configuration_async()
        {
            // SAMPLE: viewprojection-from-configuration 
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .ProjectEventAsync<MembersJoined>(e => e.QuestId, (view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .ProjectEventAsync<ProjectionEvent<MonsterSlayed>>(e => e.Data.QuestId, (view, @event) => { view.Events.Add(@event.Data); return Task.CompletedTask; })
                    .DeleteEvent<QuestEnded>()
                    .DeleteEvent<MembersDeparted>(e => e.QuestId)
                    .DeleteEvent<MonsterDestroyed>((session, e) => session.Load<QuestParty>(e.QuestId).Id);
            });
            // ENDSAMPLE 

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            await theSession.SaveChangesAsync();

            theSession.Events.Append(streamId, joined2);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            await theSession.SaveChangesAsync();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            await theSession.SaveChangesAsync();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            await theSession.SaveChangesAsync();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }

        [Fact]
        public void from_projection()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistAsyncViewProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            theSession.SaveChanges();

            theSession.Events.Append(streamId, joined2);
            theSession.SaveChanges();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }

        [Fact]
        public async void from_projection_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistAsyncViewProjection());
            });

            theSession.Events.StartStream<QuestParty>(streamId, started, joined);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(slayed1, slayed2);
            await theSession.SaveChangesAsync();

            theSession.Events.Append(streamId, joined2);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<PersistedView>(streamId);
            document.Events.Count.ShouldBe(5);
            document.Events.ShouldHaveTheSameElementsAs(started, joined, slayed1, slayed2, joined2);

            theSession.Events.Append(streamId, ended);
            await theSession.SaveChangesAsync();
            var nullDocument = theSession.Load<PersistedView>(streamId);
            nullDocument.ShouldBeNull();

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            await theSession.SaveChangesAsync();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            nullDocument2.ShouldBeNull();

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            await theSession.SaveChangesAsync();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            nullDocument3.ShouldBeNull();
        }

        [Fact]
        public void using_collection_of_ids()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<QuestView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Name = @event.Name; return Task.CompletedTask; })
                    .ProjectEventAsync<MonsterQuestsAdded>(e => e.QuestIds, (view, @event) => { view.Name = view.Name.Insert(0, $"{@event.Name}: "); return Task.CompletedTask; })
                    .DeleteEvent<MonsterQuestsRemoved>(e => e.QuestIds);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.Events.StartStream<QuestParty>(streamId2, started2);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(monsterQuestsAdded);
            theSession.SaveChanges();

            var document = theSession.Load<QuestView>(streamId);
            document.Name.ShouldStartWith(monsterQuestsAdded.Name);
            var document2 = theSession.Load<QuestView>(streamId2);
            document2.Name.ShouldStartWith(monsterQuestsAdded.Name);

            theSession.Events.StartStream<Monster>(monsterQuestsRemoved);
            theSession.SaveChanges();

            var nullDocument = theSession.Load<QuestView>(streamId);
            nullDocument.ShouldBeNull();
            var nullDocument2 = theSession.Load<QuestView>(streamId2);
            nullDocument2.ShouldBeNull();
        }

        [Fact]
        public async void using_collection_of_ids_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<QuestView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Name = @event.Name; return Task.CompletedTask; } )
                    .ProjectEventAsync<MonsterQuestsAdded>(e => e.QuestIds, (view, @event) => { view.Name = view.Name.Insert(0, $"{@event.Name}: "); return Task.CompletedTask; })
                    .DeleteEvent<MonsterQuestsRemoved>(e => e.QuestIds);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.Events.StartStream<QuestParty>(streamId2, started2);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(monsterQuestsAdded);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<QuestView>(streamId);
            document.Name.ShouldStartWith(monsterQuestsAdded.Name);
            var document2 = theSession.Load<QuestView>(streamId2);
            document2.Name.ShouldStartWith(monsterQuestsAdded.Name);

            theSession.Events.StartStream<Monster>(monsterQuestsRemoved);
            await theSession.SaveChangesAsync();

            var nullDocument = theSession.Load<QuestView>(streamId);
            nullDocument.ShouldBeNull();
            var nullDocument2 = theSession.Load<QuestView>(streamId2);
            nullDocument2.ShouldBeNull();
        }
    }
    
    // SAMPLE: viewprojection-from-class 
    public class PersistAsyncViewProjection : ViewProjection<PersistedView, Guid>
    {
        public PersistAsyncViewProjection()
        {
            ProjectEventAsync<QuestStarted>(PersistAsync);
            ProjectEventAsync<MembersJoined>(e => e.QuestId, PersistAsync);
            ProjectEventAsync<MonsterSlayed>((session, e) => session.Load<QuestParty>(e.QuestId).Id, PersistAsync);
            DeleteEvent<QuestEnded>();
            DeleteEvent<MembersDeparted>(e => e.QuestId);
            DeleteEvent<MonsterDestroyed>((session, e) => session.Load<QuestParty>(e.QuestId).Id);
        }

        private Task PersistAsync<T>(PersistedView view, T @event)
        {
            view.Events.Add(@event);
            return Task.CompletedTask;
        }
    }
    // ENDSAMPLE 
}