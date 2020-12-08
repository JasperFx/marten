using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class project_events_async_from_multiple_streams_into_view: IntegrationContext
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

        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public void from_configuration(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = tenancyStyle;
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

        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public async void from_configuration_async(TenancyStyle tenancyStyle)
        {
            // SAMPLE: viewprojection-from-configuration
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = tenancyStyle;
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
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Name = @event.Name; return Task.CompletedTask; })
                    .ProjectEventAsync<MonsterQuestsAdded>(e => e.QuestIds, (view, @event) => { view.Name = view.Name.Insert(0, $"{@event.Name}: "); return Task.CompletedTask; })
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
        public async Task verify_viewprojection_from_class_async_with_load()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<BankAccount>();
                _.Events.InlineProjections.Add<BankAccountViewProjection>();
            });

            var customer = new Customer { Id = Guid.NewGuid(), FullName = "Ron Artest" };

            var firstBankAccountId = Guid.NewGuid();
            var firstBankAccountNumber = "PL61109010140000071219812874";

            var secondBankAccountId = Guid.NewGuid();
            var secondBankAccountNumber = "PL61109010140000071219812874";

            theSession.Insert(customer);

            theSession.Events.Append(firstBankAccountId,
                new BankAccountCreated
                {
                    BankAccountId = firstBankAccountId,
                    CustomerId = customer.Id,
                    Number = firstBankAccountNumber
                });

            theSession.Events.Append(secondBankAccountId,
                new BankAccountCreated
                {
                    BankAccountId = secondBankAccountId,
                    CustomerId = customer.Id,
                    Number = secondBankAccountNumber
                });

            await theSession.SaveChangesAsync();

            var firstBankAccountView = await theSession.LoadAsync<BankAccountView>(firstBankAccountId);
            var secondBankAccountView = await theSession.LoadAsync<BankAccountView>(secondBankAccountId);

            firstBankAccountView.ShouldNotBeNull();
            firstBankAccountView.Id.ShouldBe(firstBankAccountId);
            firstBankAccountView.Number.ShouldBe(firstBankAccountNumber);

            firstBankAccountView.Customer.ShouldNotBeNull();
            firstBankAccountView.Customer.Id.ShouldBe(customer.Id);
            firstBankAccountView.Customer.FullName.ShouldBe(customer.FullName);

            secondBankAccountView.ShouldNotBeNull();
            secondBankAccountView.Id.ShouldBe(secondBankAccountId);
            secondBankAccountView.Number.ShouldBe(secondBankAccountNumber);

            secondBankAccountView.Customer.ShouldNotBeNull();
            secondBankAccountView.Customer.Id.ShouldBe(customer.Id);
            secondBankAccountView.Customer.FullName.ShouldBe(customer.FullName);

            var updatedCustomerFullName = "Metta World Peace";
            theSession.Events.Append(customer.Id,
                new CustomerFullNameUpdated
                {
                    CustomerId = customer.Id,
                    FullName = updatedCustomerFullName
                });

            await theSession.SaveChangesAsync();

            firstBankAccountView = await theSession.LoadAsync<BankAccountView>(firstBankAccountId);
            secondBankAccountView = await theSession.LoadAsync<BankAccountView>(firstBankAccountId);

            firstBankAccountView.Customer.FullName.ShouldBe(customer.FullName);
            secondBankAccountView.Customer.FullName.ShouldBe(customer.FullName);
        }

        [Fact]
        public void verify_delete_and_store_events_on_same_stream_are_processed_in_order()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .DeleteEvent<QuestEnded>();
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldNotBeNull();

            theSession.Events.Append(streamId, ended);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldBeNull();

            theSession.Events.Append(streamId, started);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldNotBeNull();

            theSession.Events.Append(streamId, ended, started);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldNotBeNull();
        }

        [Fact]
        public void verify_delete_and_with_nonexistent_streamId_does_not_throw()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<QuestParty>();
                _.Events.ProjectView<PersistedView, Guid>()
                    .ProjectEventAsync<QuestStarted>((view, @event) => { view.Events.Add(@event); return Task.CompletedTask; })
                    .DeleteEvent<QuestEnded>();
            });

            theSession.Events.StartStream<QuestParty>(streamId, started);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldNotBeNull();

            theSession.Events.Append(Guid.NewGuid(), ended);
            theSession.SaveChanges();
            theSession.Load<PersistedView>(streamId).ShouldNotBeNull();
        }

        public project_events_async_from_multiple_streams_into_view(DefaultStoreFixture fixture) : base(fixture)
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
        }
    }

    // SAMPLE: viewprojection-from-class-async
    public class PersistAsyncViewProjection: ViewProjection<PersistedView, Guid>
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

    // SAMPLE: viewprojection-from-class-async-with-load

    // Customer main aggregate
    public class Customer
    {
        public Guid Id { get; set; }

        public string FullName { get; set; }
    }

    // Event informing that customer full name was updated
    public class CustomerFullNameUpdated
    {
        public Guid CustomerId { get; set; }

        public string FullName { get; set; }
    }

    // Bank Account main aggregate
    public class BankAccount
    {
        public Guid Id { get; set; }

        // normalized reference with id to related aggregate
        public Guid CustomerId { get; set; }

        public string Number { get; set; }
    }

    //Bank Account created event with normalized data
    public class BankAccountCreated
    {
        public Guid BankAccountId { get; set; }

        public Guid CustomerId { get; set; }

        public string Number { get; set; }
    }

    // Denormalized read model with full data of related document
    public class BankAccountView
    {
        public Guid Id { get; set; }

        // Full info about customer instead of just CustomerId
        public Customer Customer { get; set; }

        public string Number { get; set; }
    }

    public class BankAccountViewProjection: ViewProjection<BankAccountView, Guid>
    {
        public BankAccountViewProjection()
        {
            ProjectEventAsync<BankAccountCreated>(e => e.BankAccountId, PersistAsync);

            // one customer might have more than one account
            Func<IDocumentSession, CustomerFullNameUpdated, List<Guid>> selectCustomerBankAccountIds =
                (ds, @event) => ds.Query<BankAccountView>()
                                  .Where(a => a.Customer.Id == @event.CustomerId)
                                  .Select(a => a.Id).ToList();

            ProjectEvent<CustomerFullNameUpdated>(selectCustomerBankAccountIds, Persist);
        }

        private async Task PersistAsync
        (
            IDocumentSession documentSession,
            BankAccountView view,
            BankAccountCreated @event
        )
        {
            // load asynchronously document to use it in denormalized view
            var customer = await documentSession.LoadAsync<Customer>(@event.CustomerId);

            view.Customer = customer;
            view.Number = @event.Number;
        }

        private void Persist(BankAccountView view, CustomerFullNameUpdated @event)
        {
            view.Customer.FullName = @event.FullName;
        }
    }

    // ENDSAMPLE
}
