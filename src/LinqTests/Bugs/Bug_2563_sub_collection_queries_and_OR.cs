using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2563_sub_collection_queries_and_OR : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2563_sub_collection_queries_and_OR(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task get_distinct_number()
    {
        theStore.Options.Schema.For<Target>()
            .Duplicate(x => x.UserIds);

        theSession.Store(new Target {Id = 1, IsPublic = false, UserIds = new [] { 1, 2, 3, 4, 5, 6 }});
        theSession.Store(new Target {Id = 2, IsPublic = false, UserIds = new int[] { }});
        theSession.Store(new Target {Id = 3, IsPublic = true, UserIds = new [] { 1, 2, 3 }});
        theSession.Store(new Target {Id = 4, IsPublic = true, UserIds = new [] { 1, 2, 6 }});
        theSession.Store(new Target {Id = 5, IsPublic = false, UserIds = new [] { 4, 5, 6 }});
        theSession.Store(new Target {Id = 6, IsPublic = true, UserIds = new [] { 10 }});

        await theSession.SaveChangesAsync();
        theSession.Logger = new TestOutputMartenLogger(_output);


        var result1 = await theSession.Query<Target>()
            .Where(x => x.IsPublic == false || x.UserIds.Contains(10))
            .ToListAsync();

        result1.Count.ShouldBeEquivalentTo(4);

        // This should pass without any error as the query will return results
        var result2 = await theSession.Query<Target>().Where(x => x.IsPublic || x.UserIds.Contains(5)).ToListAsync();

        result2.ShouldContain(x => x.Id == 1);
        result2.ShouldContain(x => x.Id == 5);
    }

    public class Target
    {
        public int Id { get; set; }

        public bool IsPublic { get; set; }

        public int[] UserIds { get; set; }
    }
}

