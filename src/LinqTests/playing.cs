using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests;

public class playing : IntegrationContext
{
    public playing(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task try_method_parsing()
    {
        var data = await theSession.Query<Target>().Where(x => x.String.EqualsIgnoreCase("something")).ToListAsync();
    }
}
