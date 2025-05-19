using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Diagnostics;

public class ability_to_fetch_postgres_server_version: IntegrationContext
{
    [Fact]
    public void can_fetch_postgres_server_version()
    {
        #region sample_get_postgres_version
        var pgVersion = theStore.Diagnostics.GetPostgresVersion();
        #endregion
        pgVersion.ShouldNotBeNull();
    }

    public ability_to_fetch_postgres_server_version(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
