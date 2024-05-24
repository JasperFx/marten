using EventSourcingTests.Aggregation;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2978_aggregate_projection_documents_are_not_optimistic_concurrency_event_with_that_policy : BugIntegrationContext
{
    [Fact]
    public void override_the_optimistic_concurrency_on_projected_document()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsEnforceOptimisticConcurrency();
            opts.Projections.Add<AllGood>(ProjectionLifecycle.Async);
        });

        var mapping = theStore.Options.Storage.MappingFor(typeof(MyAggregate));
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.UseOptimisticConcurrency.ShouldBeFalse();

        theStore.Options.Storage.MappingFor(typeof(Target))
            .UseOptimisticConcurrency.ShouldBeTrue();
    }
}
