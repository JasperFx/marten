using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Acceptance;

public class using_explicit_sql_in_select_clauses : IntegrationContext
{
    public using_explicit_sql_in_select_clauses(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task using_explicit_sql_to_scalar_value()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });


        var firstNames = await theSession.Query<User>().Where(x => x.FirstName == "Hank")
            .Select(x => x.ExplicitSql<string>("d.data -> 'FirstName'"))
            .ToListAsync();

        firstNames.All(x => x == "Hank").ShouldBeTrue();
    }

    [Fact]
    public async Task using_explicit_sql_in_transform()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });


        var firstNames = await theSession.Query<User>().Where(x => x.FirstName == "Hank")
            .Select(x => new {Name = x.ExplicitSql<string>("d.data -> 'FirstName'")})
            .ToListAsync();

        firstNames.All(x => x.Name == "Hank").ShouldBeTrue();
    }
}
