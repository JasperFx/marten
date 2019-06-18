using Xunit;

namespace Marten.Testing
{
    public class ability_to_fetch_postgres_server_version: IntegratedFixture
    {
        [Fact]
        public void can_fetch_postgres_server_version()
        {
            // SAMPLE: get_postgres_version
            var pgVersion = theStore.Diagnostics.GetPostgresVersion();
            // ENDSAMPLE
            pgVersion.ShouldNotBeNull();
        }
    }
}
