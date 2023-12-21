using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class using_explicit_sql_in_select_clauses : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public using_explicit_sql_in_select_clauses(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task using_explicit_sql_to_scalar_value()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

        theSession.Logger = new TestOutputMartenLogger(_output);

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

        theSession.Logger = new TestOutputMartenLogger(_output);

        var firstNames = await theSession.Query<User>().Where(x => x.FirstName == "Hank")
            .Select(x => new {Name = x.ExplicitSql<string>("d.data -> 'FirstName'")})
            .ToListAsync();

        firstNames.All(x => x.Name == "Hank").ShouldBeTrue();
    }
}
