using System.Threading.Tasks;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_499_event_store_objects_should_not_be_erroneously_in_the_patch: BugIntegrationContext
    {
        [Fact]
        public async Task not_using_the_event_store_should_not_be_in_patch()
        {
            StoreOptions(_ => _.Schema.For<User>());

            var patch = await theStore.Schema.CreateMigrationAsync();

            patch.UpdateSql().ShouldNotContain("mt_events");
            patch.UpdateSql().ShouldNotContain("mt_streams");
        }

    }
}
