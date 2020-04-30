using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class ability_to_fetch_postgres_server_version: IntegrationContext
    {
        [Fact]
        public void can_fetch_postgres_server_version()
        {
            // SAMPLE: get_postgres_version
            var pgVersion = theStore.Diagnostics.GetPostgresVersion();
            // ENDSAMPLE
            pgVersion.ShouldNotBeNull();
        }

        public ability_to_fetch_postgres_server_version(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
