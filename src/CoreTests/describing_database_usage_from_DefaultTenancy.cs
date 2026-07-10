using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

public class describing_database_usage_from_DefaultTenancy : IntegrationContext
{
    public describing_database_usage_from_DefaultTenancy(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task create_usage()
    {
        theStore.Options.Tenancy.ShouldBeOfType<DefaultTenancy>();
        theStore.Options.Tenancy.Cardinality.ShouldBe(DatabaseCardinality.Single);
        var description = await theStore.Options.Tenancy.DescribeDatabasesAsync(CancellationToken.None);

        description.Cardinality.ShouldBe(DatabaseCardinality.Single);

        // Derive the database and server from whatever the suite is actually pointed at, rather than hard
        // coding the values in the Marten docker compose file. `marten_testing_database` is a documented way
        // to run against a different database, and this was the one test that did not honor it.
        var expected = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        description.MainDatabase.DatabaseName.ShouldBe(expected.Database);
        description.MainDatabase.ServerName.ShouldBe(expected.Host);

        // These two are properties of the store, not of the connection: DefaultStoreFixture leaves
        // DatabaseSchemaName at its default, and the provider is always Postgres.
        description.MainDatabase.SchemaOrNamespace.ShouldBe("public");
        description.MainDatabase.Engine.ShouldBe("PostgreSQL");

        description.Databases.Any().ShouldBeFalse();
    }
}
