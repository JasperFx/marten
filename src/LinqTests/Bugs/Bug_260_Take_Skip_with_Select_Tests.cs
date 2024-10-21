using System.Linq;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_260_Take_Skip_with_Select_Tests: IntegrationContext
{
    [Fact]
    public void return_the_correct_number_of_results()
    {
        var targets = Target.GenerateRandomData(100);
        theStore.BulkInsert(targets.ToArray());

        var queryable = theSession.Query<Target>()
            .Skip(10)
            .Take(10)
            .Select(entity => entity.Id);

        var cmd = queryable.ToCommand(FetchType.FetchMany);

        cmd.CommandText.ShouldContain("LIMIT :p1");

        cmd.Parameters["p1"].Value.ShouldBe(10);

        queryable.ToArray().Length.ShouldBe(10);
    }

    public Bug_260_Take_Skip_with_Select_Tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
