using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Testing.Harness;
using NSubstitute;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class AggregateFinderTests
    {
        [Fact]
        public void find_when_stream_is_new()
        {
            var session = Substitute.For<IDocumentSession>();

            var finder = new AggregateFinder<QuestParty>();

            var id = Guid.NewGuid();
            finder.Find(new EventStream(id, true), session)
                .ShouldNotBeNull();

            session.DidNotReceive().Load<QuestParty>(id);
        }

        [Fact]
        public void find_when_stream_is_not_new()
        {
            var session = Substitute.For<IDocumentSession>();
            var id = Guid.NewGuid();

            var persisted = new QuestParty { Id = id };

            var finder = new AggregateFinder<QuestParty>();

            session.Load<QuestParty>(id).Returns(persisted);

            finder.Find(new EventStream(id, false), session)
                .ShouldBeTheSameAs(persisted);
        }

        [Fact]
        public void find_when_stream_is_not_new_and_it_is_not_in_database()
        {
            var session = Substitute.For<IDocumentSession>();
            var id = Guid.NewGuid();

            var finder = new AggregateFinder<QuestParty>();

            finder.Find(new EventStream(id, false), session)
                .ShouldNotBeNull();
        }
    }

    public class AggregateFinder_Async: IntegrationContext
    {
        [Fact]
        public async Task find_when_stream_is_new_async()
        {
            var finder = new AggregateFinder<QuestParty>();

            var id = Guid.NewGuid();
            (await finder.FindAsync(new EventStream(id, true), theSession, new CancellationToken()))
                .ShouldNotBeNull();
        }

        [Fact]
        public async Task find_when_stream_is_not_new_async()
        {
            var id = Guid.NewGuid();

            var persisted = new QuestParty { Id = id };
            theSession.Store(persisted);
            await theSession.SaveChangesAsync();

            var finder = new AggregateFinder<QuestParty>();

            (await finder.FindAsync(new EventStream(id, false), theSession, new CancellationToken()))
                .ShouldBeTheSameAs(persisted);
        }

        [Fact]
        public async Task find_when_stream_is_not_new_and_it_is_not_in_database_async()
        {
            var id = Guid.NewGuid();

            var finder = new AggregateFinder<QuestParty>();

            (await finder.FindAsync(new EventStream(id, false), theSession, new CancellationToken()))
                .ShouldNotBeNull();
        }

        public AggregateFinder_Async(DefaultStoreFixture fixture) : base(fixture)
        {
            DocumentTracking = DocumentTracking.IdentityOnly;
        }
    }
}
