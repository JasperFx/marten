using System.Linq;
using Marten.Events.Projections;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Aggregation
{
    public class aggregates_should_be_registered_as_document_mappings_automatically: IntegrationContext
    {
        [Fact]
        public void aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Projections.AggregatorFor<QuestParty>();
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }


        [Fact]
        public void inline_aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Projections.SelfAggregate<QuestParty>();
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }

        [Fact]
        public void async_aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Projections.SelfAggregate<QuestParty>(ProjectionLifecycle.Async);
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }


        public aggregates_should_be_registered_as_document_mappings_automatically(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
