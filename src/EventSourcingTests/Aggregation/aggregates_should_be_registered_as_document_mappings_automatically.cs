using System.Linq;
using EventSourcingTests.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation
{
    public class aggregates_should_be_registered_as_document_mappings_automatically: OneOffConfigurationsContext
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
        public void aggregate_projections_should_automatically_register_the_aggregate_document_type()
        {
            StoreOptions(opts =>
            {
                opts.Projections.Add<AllGood>();
            });

            // MyAggregate is the aggregate type for AllGood above
            theStore.Storage.AllDocumentMappings.Select(x => x.DocumentType)
                .ShouldContain(typeof(MyAggregate));
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

    }
}
