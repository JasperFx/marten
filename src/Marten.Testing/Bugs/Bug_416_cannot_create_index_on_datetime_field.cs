using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_416_cannot_create_index_on_datetime_field: BugIntegrationContext
    {
        [Fact]
        public void should_throw_a_defensive_check_telling_you_that_you_cannot_index_a_date_time_field()
        {
            StoreOptions(_ => _.Schema.For<Target>().Index(x => x.Date));

            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));
        }

    }
}
