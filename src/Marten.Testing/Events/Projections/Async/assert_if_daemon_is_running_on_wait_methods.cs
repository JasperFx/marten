using System;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Marten.Testing.CodeTracker;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class assert_if_daemon_is_running_on_wait_methods: IntegrationContext
    {
        private IDaemon theDaemon;

        public assert_if_daemon_is_running_on_wait_methods(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ =>
            {
                _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>();
                _.Events.AsyncProjections.TransformEvents(new CommitViewTransform());
            });

            theDaemon = theStore.BuildProjectionDaemon();
        }

        [Fact]
        public async Task wait_for_non_stale_results_of_all()
        {
            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theDaemon.WaitForNonStaleResults();
            });
        }

        [Fact]
        public async Task wait_for_non_stale_results_of_all_to_revision_number()
        {
            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theDaemon.WaitUntilEventIsProcessed(200);
            });
        }

        [Fact]
        public async Task wait_for_non_stale_results_of_a_specific_view()
        {
            await Exception<InvalidOperationException>.ShouldBeThrownByAsync(async () =>
            {
                await theDaemon.WaitForNonStaleResultsOf(typeof(ActiveProject));
            });
        }
    }
}
