using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class string_compare_operator: IntegrationContext
{
    [Fact]
    public async Task string_compare_works()
    {
        theSession.Store(new Target { String = "Apple" });
        theSession.Store(new Target { String = "Banana" });
        theSession.Store(new Target { String = "Cherry" });
        theSession.Store(new Target { String = "Durian" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target>().Where(x => string.Compare(x.String, "Cherry") > 0);

        queryable.ToList().Count.ShouldBe(1);
    }

    [Fact]
    public async Task string_compare_to_works()
    {
        theSession.Store(new Target { String = "Apple" });
        theSession.Store(new Target { String = "Banana" });
        theSession.Store(new Target { String = "Cherry" });
        theSession.Store(new Target { String = "Durian" });
        await theSession.SaveChangesAsync();

        var queryable = theSession.Query<Target>().Where(x => x.String.CompareTo("Banana") > 0);

        queryable.ToList().Count.ShouldBe(2);
    }

    public string_compare_operator(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
