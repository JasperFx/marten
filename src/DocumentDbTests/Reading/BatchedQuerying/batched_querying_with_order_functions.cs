using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Reading.BatchedQuerying;

public class batched_querying_with_order_functions: IntegrationContext
{
    [Fact]
    public async Task orderby_thenby()
    {
        var batch = theSession.CreateBatchQuery();

        var toList = batch.Query<User>().OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Select(x => new { x.FirstName, x.LastName }).ToList();

        await batch.Execute();

        var names = await toList;
        names.Select(x => x.FirstName).ShouldHaveTheSameElementsAs("Harry", "Harry", "Justin", "Justin", "Michael", "Michael");
        names.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Smith", "Somerset", "Houston", "White", "Bean", "Brown");
    }

    [Fact]
    public async Task orderbydescending_thenby()
    {
        var batch = theSession.CreateBatchQuery();

        var toList = batch.Query<User>().OrderByDescending(x => x.FirstName).ThenBy(x => x.LastName).Select(x => new { x.FirstName, x.LastName }).ToList();

        await batch.Execute();

        var names = await toList;
        names.Select(x => x.FirstName).ShouldHaveTheSameElementsAs("Michael", "Michael", "Justin", "Justin", "Harry", "Harry");
        names.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Bean", "Brown", "Houston", "White", "Smith", "Somerset");
    }

    [Fact]
    public async Task orderby_thenbydescending()
    {
        var batch = theSession.CreateBatchQuery();

        var toList = batch.Query<User>().OrderBy(x => x.FirstName).ThenByDescending(x => x.LastName).Select(x => new { x.FirstName, x.LastName }).ToList();

        await batch.Execute();

        var names = await toList;
        names.Select(x => x.FirstName).ShouldHaveTheSameElementsAs("Harry", "Harry", "Justin", "Justin", "Michael", "Michael");
        names.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Somerset", "Smith", "White", "Houston", "Brown", "Bean");
    }

    [Fact]
    public async Task orderbydescending_thenbydescending()
    {
        var batch = theSession.CreateBatchQuery();

        var toList = batch.Query<User>().OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName).Select(x => new { x.FirstName, x.LastName }).ToList();

        await batch.Execute();

        var names = await toList;
        names.Select(x => x.FirstName).ShouldHaveTheSameElementsAs("Michael", "Michael", "Justin", "Justin", "Harry", "Harry");
        names.Select(x => x.LastName).ShouldHaveTheSameElementsAs("Brown", "Bean", "White", "Houston", "Somerset", "Smith");
    }

    protected override Task fixtureSetup()
    {
        theSession.Store(
            new User { FirstName = "Justin", LastName = "Houston" },
            new User { FirstName = "Justin", LastName = "White" },
            new User { FirstName = "Michael", LastName = "Bean" },
            new User { FirstName = "Michael", LastName = "Brown" },
            new User { FirstName = "Harry", LastName = "Smith" },
            new User { FirstName = "Harry", LastName = "Somerset" }
        );

        return theSession.SaveChangesAsync();
    }

    public batched_querying_with_order_functions(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}