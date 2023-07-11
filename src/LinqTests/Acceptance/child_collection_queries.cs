using System.Linq;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Marten.Testing.Documents;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class child_collection_queries: LinqTestContext<child_collection_queries>
{
    public child_collection_queries(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    static child_collection_queries()
    {
        // Child document collections
        @where(x => x.Children.Any()).NoCteUsage();

        // CTE required queries
        @where(x => x.Children.Where(c => c.String.StartsWith("o")).Any());
        @where(x => x.Children.Any(c => c.String.StartsWith("o")));

        // CTE + Filter at parent
        @where(x => x.Color == Colors.Blue && x.Children.Any(c => c.String.StartsWith("o")));

        // CTE + filter has to stay at the bottom level
        @where(x => x.Color == Colors.Blue || x.Children.Any(c => c.String.StartsWith("o")));

        // Child value collections
        @where(x => x.NumberArray.Any());

        @where(x => x.StringArray != null && x.StringArray.Any(c => c.StartsWith("o")));
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertTestCase(description, Fixture.Store);
    }
}


