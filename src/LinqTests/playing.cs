using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests;

public class playing : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public playing(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task try_method_parsing()
    {
        theSession.Logger = new TestOutputMartenLogger(_output);
        var data = await theSession.Query<Target>().Where(x => x.String.ToLower().IsOneOf("red", "blue")).ToListAsync();
    }
}
