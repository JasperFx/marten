using System.Linq;
using EventSourcingTests.Projections;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class aggregates_should_be_registered_as_document_mappings_automatically: OneOffConfigurationsContext
{
    [Fact]
    public void aggregations_are_registered()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
        });

        var types = theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType).ToList();
        types
            .ShouldContain(typeof(QuestParty));
    }

    [Fact]
    public void aggregate_projections_should_automatically_register_the_aggregate_document_type()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<AllGood>(ProjectionLifecycle.Inline);
        });

        // MyAggregate is the aggregate type for AllGood above
        theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType)
            .ShouldContain(typeof(MyAggregate));
    }


    [Fact]
    public void inline_aggregations_are_registered()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
        });

        theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType)
            .ShouldContain(typeof(QuestParty));
    }

    [Fact]
    public void async_aggregations_are_registered()
    {
        StoreOptions(_ =>
        {
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Async);
        });

        theStore.StorageFeatures.AllDocumentMappings.Select(x => x.DocumentType)
            .ShouldContain(typeof(QuestParty));
    }
}
