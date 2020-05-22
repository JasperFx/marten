using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class project_events_from_multiple_streams_into_view: IntegrationContextWithIdentityMap<IdentityMap>
    {
        private static readonly Guid streamId = Guid.NewGuid();
        private static readonly Guid streamId2 = Guid.NewGuid();

        private QuestStarted started = new QuestStarted { Id = streamId, Name = "Find the Orb" };
        private QuestStarted started2 = new QuestStarted { Id = streamId2, Name = "Find the Orb 2.0" };
        private MonsterQuestsAdded monsterQuestsAdded = new MonsterQuestsAdded { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
        private MonsterQuestsRemoved monsterQuestsRemoved = new MonsterQuestsRemoved { QuestIds = new List<Guid> { streamId, streamId2 }, Name = "Dragon" };
        private QuestEnded ended = new QuestEnded { Id = streamId, Name = "Find the Orb" };
        private MembersJoined joined = new MembersJoined { QuestId = streamId, Day = 2, Location = "Faldor's Farm", Members = new[] { "Garion", "Polgara", "Belgarath" } };
        private MonsterSlayed slayed1 = new MonsterSlayed { QuestId = streamId, Name = "Troll" };
        private MonsterSlayed slayed2 = new MonsterSlayed { QuestId = streamId, Name = "Dragon" };
        private MonsterDestroyed destroyed = new MonsterDestroyed { QuestId = streamId, Name = "Troll" };
        private MembersDeparted departed = new MembersDeparted { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };
        private MembersJoined joined2 = new MembersJoined { QuestId = streamId, Day = 5, Location = "Sendaria", Members = new[] { "Silk", "Barak" } };

        [Fact]
        public void from_configuration()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEvent<ProjectionEvent<QuestStarted>>((view, @event) => { view.Events.Add(@event.Data); view.StreamIdsForEvents.Add(@event.StreamId); })
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => view.Events.Add(@event))
                    .ProjectEvent<ProjectionEvent<MonsterSlayed>>(e => e.Data.QuestId, (view, @event) => { view.Events.Add(@event.Data); view.StreamIdsForEvents.Add(@event.StreamId); })
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
            SpecificationExtensions.ShouldBeNull(nullDocument);

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument2);

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument3);

            // Add document back to see if we can project stream ids from event handlers (Applies to other IEvent properties)
            theSession.Events.Append(streamId, started, joined);
            var monsterId = Guid.NewGuid();
            theSession.Events.StartStream(monsterId, slayed1);
            theSession.SaveChanges();
            var document4 = theSession.Load<PersistedView>(streamId);
            document4.StreamIdsForEvents.Count.ShouldBe(2); // Ids of the two streams
            document4.StreamIdsForEvents.Contains(streamId).ShouldBeTrue();
            document4.StreamIdsForEvents.Contains(monsterId).ShouldBeTrue();

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            var nullDocument4 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument4);
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
                    .ProjectEvent<ProjectionEvent<QuestStarted>>((view, @event) => { view.Events.Add(@event.Data); view.StreamIdsForEvents.Add(@event.StreamId); })
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => { view.Events.Add(@event); })
                    .ProjectEvent<ProjectionEvent<MonsterSlayed>>(e => e.Data.QuestId, (view, @event) => { view.Events.Add(@event.Data); view.StreamIdsForEvents.Add(@event.StreamId); })
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
            SpecificationExtensions.ShouldBeNull(nullDocument);

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            await theSession.SaveChangesAsync();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument2);

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            await theSession.SaveChangesAsync();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument3);

            // Add document back to see if we can project stream ids from event handlers (Applies to other IEvent properties)
            theSession.Events.Append(streamId, started, joined);
            var monsterId = Guid.NewGuid();
            theSession.Events.StartStream(monsterId, slayed1);
            await theSession.SaveChangesAsync();
            var document4 = theSession.Load<PersistedView>(streamId);
            document4.StreamIdsForEvents.Count.ShouldBe(2); // Ids of the two streams
            document4.StreamIdsForEvents.Contains(streamId).ShouldBeTrue();
            document4.StreamIdsForEvents.Contains(monsterId).ShouldBeTrue();

            theSession.Events.Append(streamId, ended);
            await theSession.SaveChangesAsync();
            var nullDocument4 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument4);
        }

        [Fact]
        public void from_projection()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistViewProjection());
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
            SpecificationExtensions.ShouldBeNull(nullDocument);

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            theSession.SaveChanges();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument2);

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            theSession.SaveChanges();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument3);
        }

        [Fact]
        public async void from_projection_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.InlineProjections.Add(new PersistViewProjection());
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
            SpecificationExtensions.ShouldBeNull(nullDocument);

            // Add document back to so we can delete it by selector
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document2 = theSession.Load<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, departed);
            await theSession.SaveChangesAsync();
            var nullDocument2 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument2);

            // Add document back to so we can delete it by other selector type
            theSession.Events.Append(streamId, started);
            await theSession.SaveChangesAsync();
            var document3 = theSession.Load<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(1);

            theSession.Events.Append(streamId, destroyed);
            await theSession.SaveChangesAsync();
            var nullDocument3 = theSession.Load<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument3);
        }

        [Fact]
        public void using_collection_of_ids()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<QuestView, Guid>()
                    .ProjectEvent<QuestStarted>((view, @event) => view.Name = @event.Name)
                    .ProjectEvent<MonsterQuestsAdded>(e => e.QuestIds, (view, @event) => view.Name = view.Name.Insert(0, $"{@event.Name}: "))
                    .DeleteEvent<MonsterQuestsRemoved>(e => e.QuestIds);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.Events.StartStream<QuestParty>(streamId2, started2);
            theSession.SaveChanges();

            theSession.Events.StartStream<Monster>(monsterQuestsAdded);
            theSession.SaveChanges();

            var document = theSession.Load<QuestView>(streamId);
            SpecificationExtensions.ShouldStartWith(document.Name, monsterQuestsAdded.Name);
            var document2 = theSession.Load<QuestView>(streamId2);
            SpecificationExtensions.ShouldStartWith(document2.Name, monsterQuestsAdded.Name);

            theSession.Events.StartStream<Monster>(monsterQuestsRemoved);
            theSession.SaveChanges();

            var nullDocument = theSession.Load<QuestView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument);
            var nullDocument2 = theSession.Load<QuestView>(streamId2);
            SpecificationExtensions.ShouldBeNull(nullDocument2);
        }

        [Fact]
        public async void using_collection_of_ids_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<QuestView, Guid>()
                    .ProjectEvent<QuestStarted>((view, @event) => view.Name = @event.Name)
                    .ProjectEvent<MonsterQuestsAdded>(e => e.QuestIds, (view, @event) => view.Name = view.Name.Insert(0, $"{@event.Name}: "))
                    .DeleteEvent<MonsterQuestsRemoved>(e => e.QuestIds);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.Events.StartStream<QuestParty>(streamId2, started2);
            await theSession.SaveChangesAsync();

            theSession.Events.StartStream<Monster>(monsterQuestsAdded);
            await theSession.SaveChangesAsync();

            var document = theSession.Load<QuestView>(streamId);
            SpecificationExtensions.ShouldStartWith(document.Name, monsterQuestsAdded.Name);
            var document2 = theSession.Load<QuestView>(streamId2);
            SpecificationExtensions.ShouldStartWith(document2.Name, monsterQuestsAdded.Name);

            theSession.Events.StartStream<Monster>(monsterQuestsRemoved);
            await theSession.SaveChangesAsync();

            var nullDocument = theSession.Load<QuestView>(streamId);
            SpecificationExtensions.ShouldBeNull(nullDocument);
            var nullDocument2 = theSession.Load<QuestView>(streamId2);
            SpecificationExtensions.ShouldBeNull(nullDocument2);
        }

        [Fact]
        public async Task updateonly_event_should_not_create_new_document()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEvent<QuestStarted>((view, @event) => view.Events.Add(@event))
                    .ProjectEvent<MembersJoined>(e => e.QuestId, (view, @event) => view.Events.Add(@event))
                    .ProjectEvent<MonsterSlayed>(e =>
                    {
                        return Guid.NewGuid();
                    }, (view, @event) => view.Events.Add(@event), onlyUpdate: true);
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.SaveChanges();

            var document = await theSession.LoadAsync<PersistedView>(streamId);
            document.Events.Count.ShouldBe(1);

            theSession.Events.StartStream<Monster>(slayed1);
            theSession.SaveChanges();

            var document2 = await theSession.LoadAsync<PersistedView>(streamId);
            document2.Events.Count.ShouldBe(1);

            theSession.Events.StartStream<QuestParty>(joined);
            theSession.SaveChanges();

            var document3 = await theSession.LoadAsync<PersistedView>(streamId);
            document3.Events.Count.ShouldBe(2);
        }

        [Fact]
        public async Task updateonly_event_for_custom_view_projection_should_not_create_new_document()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
                _.Events.InlineProjections.Add<NewsletterSubscriptionProjection>();
            });

            var subscriptionId = Guid.NewGuid();
            var newsletterId = Guid.NewGuid();
            var readerId = Guid.NewGuid();

            var readerSubscribed = new ReaderSubscribed(subscriptionId, newsletterId, readerId, "John Doe");
            theSession.Events.StartStream<NewsletterSubscription>(streamId, readerSubscribed);
            await theSession.SaveChangesAsync();
            var subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
            subscription.ShouldNotBeNull();

            var newsletterOpened = new NewsletterOpened(subscriptionId, DateTime.Now);
            theSession.Events.Append(subscriptionId, newsletterOpened);
            await theSession.SaveChangesAsync();
            subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
            subscription.ShouldNotBeNull();

            var readerUnsubscribed = new ReaderUnsubscribed(subscriptionId);
            theSession.Events.Append(subscriptionId, readerUnsubscribed);
            await theSession.SaveChangesAsync();
            subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
            subscription.ShouldBeNull();

            var newsletterOpenedAfterUnsubscribe = new NewsletterOpened(subscriptionId, DateTime.Now);
            theSession.Events.Append(subscriptionId, newsletterOpenedAfterUnsubscribe);
            await theSession.SaveChangesAsync();
            subscription = await theSession.LoadAsync<NewsletterSubscription>(subscriptionId);
            subscription.ShouldBeNull();
        }

        [Fact]
        public async Task default_id_event_should_not_create_new_document()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEvent<QuestStarted>(e =>
                    {
                        return Guid.Empty;
                    }, (view, @event) => view.Events.Add(@event));
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.SaveChanges();

            var document = await theSession.LoadAsync<PersistedView>(streamId);
            SpecificationExtensions.ShouldBeNull(document);

            var documentCount = await theSession.Query<PersistedView>().CountAsync();
            documentCount.ShouldBe(0);
        }

        [Fact]
        public async Task projectview_withdeleteevent_should_be_respected_during_projection_rebuild()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<Project>();
                _.Events.ProjectView<Project, Guid>().DeleteEvent<ProjectClosed>();
            });

            var projectId = Guid.NewGuid();

            theSession.Events.Append(projectId, new ProjectStarted { Id = projectId, Name = "Marten"});
            theSession.SaveChanges();

            theSession.Query<Project>().Count().ShouldBe(1);

            theSession.Events.Append(projectId, new ProjectClosed { Id = projectId });
            theSession.SaveChanges();

            theSession.Query<Project>().Count().ShouldBe(0);

            var settings = new DaemonSettings
            {
                LeadingEdgeBuffer = 0.Seconds()
            };

            using (var daemon = theStore.BuildProjectionDaemon(new[] {typeof(User)}, settings: settings))
            {
                await daemon.RebuildAll();
            }

            //fails as user is back in the database after rebuilding
            theSession.Query<Project>().Count().ShouldBe(0);

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.SaveChanges();

            var document = await theSession.LoadAsync<PersistedView>(streamId);
            document.ShouldBeNull();

            var documentCount = await theSession.Query<PersistedView>().CountAsync();
            documentCount.ShouldBe(0);
        }

        public project_events_from_multiple_streams_into_view(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class QuestView
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class PersistedView
    {
        public Guid Id { get; set; }
        public List<object> Events { get; } = new List<object>();
        public List<Guid> StreamIdsForEvents { get; set; } = new List<Guid>();
    }

    // SAMPLE: viewprojection-from-class
    public class PersistViewProjection: ViewProjection<PersistedView, Guid>
    {
        public PersistViewProjection()
        {
            ProjectEvent<QuestStarted>(Persist);
            ProjectEvent<MembersJoined>(e => e.QuestId, Persist);
            ProjectEvent<MonsterSlayed>((session, e) => session.Load<QuestParty>(e.QuestId).Id, Persist);
            DeleteEvent<QuestEnded>();
            DeleteEvent<MembersDeparted>(e => e.QuestId);
            DeleteEvent<MonsterDestroyed>((session, e) => session.Load<QuestParty>(e.QuestId).Id);
        }

        private void Persist<T>(PersistedView view, T @event)
        {
            view.Events.Add(@event);
        }
    }

    // ENDSAMPLE

    // SAMPLE: viewprojection-from-class-with-eventdata

    public class Lap
    {
        public Guid Id { get; set; }

        public DateTimeOffset? Start { get; set; }

        public DateTimeOffset? End { get; set; }
    }

    public class LapStarted
    {
        public Guid LapId { get; set; }
    }

    public class LapFinished
    {
        public Guid LapId { get; set; }
    }

    public class LapViewProjection: ViewProjection<Lap, Guid>
    {
        public LapViewProjection()
        {
            ProjectEvent<ProjectionEvent<LapStarted>>(e => e.Data.LapId, Persist);
            ProjectEvent<ProjectionEvent<LapFinished>>(e => e.Data.LapId, Persist);
        }

        private void Persist(Lap view, ProjectionEvent<LapStarted> eventData)
        {
            view.Start = eventData.Timestamp;
        }

        private void Persist(Lap view, ProjectionEvent<LapFinished> eventData)
        {
            view.End = eventData.Timestamp;
        }
    }

    // ENDSAMPLE

    // SAMPLE: viewprojection-with-update-only

    public class NewsletterSubscription
    {
        public Guid Id { get; set; }

        public Guid NewsletterId { get; set; }

        public Guid ReaderId { get; set; }

        public string FirstName { get; set; }

        public int OpensCount { get; set; }
    }

    public class ReaderSubscribed
    {
        public Guid SubscriptionId { get; }

        public Guid NewsletterId { get; }

        public Guid ReaderId { get; }

        public string FirstName { get; }

        public ReaderSubscribed(Guid subscriptionId, Guid newsletterId, Guid readerId, string firstName)
        {
            SubscriptionId = subscriptionId;
            NewsletterId = newsletterId;
            ReaderId = readerId;
            FirstName = firstName;
        }
    }

    public class NewsletterOpened
    {
        public Guid SubscriptionId { get; }

        public DateTime OpenedAt { get; }

        public NewsletterOpened(Guid subscriptionId, DateTime openedAt)
        {
            SubscriptionId = subscriptionId;
            OpenedAt = openedAt;
        }
    }

    public class ReaderUnsubscribed
    {
        public Guid SubscriptionId { get; }

        public ReaderUnsubscribed(Guid subscriptionId)
        {
            SubscriptionId = subscriptionId;
        }
    }

    public class NewsletterSubscriptionProjection : ViewProjection<NewsletterSubscription, Guid>
    {
        public NewsletterSubscriptionProjection()
        {
            ProjectEvent<ReaderSubscribed>(@event => @event.SubscriptionId, Persist);
            ProjectEvent<NewsletterOpened>(@event => @event.SubscriptionId, Persist, onlyUpdate: true);
            DeleteEvent<ReaderUnsubscribed>(@event => @event.SubscriptionId);
        }

        private void Persist(NewsletterSubscription view, ReaderSubscribed @event)
        {
            view.Id = @event.SubscriptionId;
            view.NewsletterId = @event.NewsletterId;
            view.ReaderId = @event.ReaderId;
            view.FirstName = @event.FirstName;
            view.OpensCount = 0;
        }

        private void Persist(NewsletterSubscription view, NewsletterOpened @event)
        {
            view.OpensCount++;
        }
    }

    // ENDSAMPLE

    public class Project
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public void Apply(ProjectStarted e)
        {
            Id = e.Id;
            Name = e.Name;
        }

        public void Apply(ProjectClosed e)
        {
        }
    }

    public class ProjectStarted
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
    }

    public class ProjectClosed
    {
        public Guid Id { get; set; }
    }
}
