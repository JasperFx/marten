using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_1413_not_inside_of_where_against_child_collection : IntegrationContext
{
    public Bug_1413_not_inside_of_where_against_child_collection(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void can_do_so()
    {
        var results = theSession.Query<Target>().Where(x => x.Children.Any(c => c.String == "hello" && c.Color != Colors.Blue))
            .ToList();

        results.ShouldNotBeNull();
    }
}
