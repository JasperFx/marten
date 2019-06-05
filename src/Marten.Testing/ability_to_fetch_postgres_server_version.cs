using Xunit;

namespace Marten.Testing
{
    public class ability_to_fetch_postgres_server_version: IntegratedFixture
    {
        [Fact]
        public void can_fetch_postgres_server_version()
        {
            var pgVersion = theStore.GetPostgresVersion();
            pgVersion.ShouldNotBeNull();
        }
    }
}
