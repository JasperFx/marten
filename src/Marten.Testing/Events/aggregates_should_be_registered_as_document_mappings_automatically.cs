using System.Linq;
using Marten.Events.Projections;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class aggregates_should_be_registered_as_document_mappings_automatically: IntegrationContext
    {
        [Fact]
        public void aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Events.Projections.AggregatorFor<QuestParty>();
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }


        [Fact]
        public void inline_aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Events.Projections.InlineSelfAggregate<QuestParty>();
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }

        [Fact]
        public void async_aggregations_are_registered()
        {
            StoreOptions(_ =>
            {
                _.Events.Projections.AsyncSelfAggregate<QuestParty>();
            });

            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(QuestParty));
        }


        public aggregates_should_be_registered_as_document_mappings_automatically(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
