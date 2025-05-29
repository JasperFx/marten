using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using Marten.Storage;
using Marten.Testing.Harness;
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
        theStore.Options.Tenancy.As<IDatabaseUser>().Cardinality.ShouldBe(DatabaseCardinality.Single);
        var description = await theStore.Options.Tenancy.DescribeDatabasesAsync(CancellationToken.None);

        description.Cardinality.ShouldBe(DatabaseCardinality.Single);

        // ignore this if you aren't using the Marten Docker compose file
        description.MainDatabase.DatabaseName.ShouldBe("marten_testing");
        description.MainDatabase.SchemaOrNamespace.ShouldBe("public");
        description.MainDatabase.Engine.ShouldBe("PostgreSQL");
        description.MainDatabase.ServerName.ShouldBe("localhost");

        description.Databases.Any().ShouldBeFalse();
    }
}
