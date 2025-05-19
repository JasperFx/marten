using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance;

public class chained_where_clauses : IntegrationContext
{
    [Fact]
    public async Task two_where_clauses()
    {
        var target1 = new Target{Number = 1, String = "Foo"};
        var target2 = new Target{Number = 2, String = "Foo"};
        var target3 = new Target{Number = 1, String = "Bar"};
        var target4 = new Target{Number = 1, String = "Foo"};
        var target5 = new Target{Number = 2, String = "Bar"};
        theSession.Store(target1);
        theSession.Store(target2);
        theSession.Store(target3);
        theSession.Store(target4);
        theSession.Store(target5);
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.Number == 1).Where(x => x.String == "Foo").ToArray()
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(target1.Id, target4.Id);
    }

    [Fact]
    public async Task three_where_clauses()
    {
        var target1 = new Target { Number = 1, String = "Foo", Long = 5};
        var target2 = new Target { Number = 2, String = "Foo" };
        var target3 = new Target { Number = 1, String = "Bar" };
        var target4 = new Target { Number = 1, String = "Foo" };
        var target5 = new Target { Number = 2, String = "Bar" };
        theSession.Store(target1);
        theSession.Store(target2);
        theSession.Store(target3);
        theSession.Store(target4);
        theSession.Store(target5);
        await theSession.SaveChangesAsync();

        theSession.Query<Target>().Where(x => x.Number == 1).Where(x => x.String == "Foo").Where(x => x.Long == 5).ToArray()
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(target1.Id);
    }

    public chained_where_clauses(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
