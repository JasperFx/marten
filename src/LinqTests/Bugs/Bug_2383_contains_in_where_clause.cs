using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2383_contains_in_where_clause : BugIntegrationContext
{
    [Fact]
    public async Task can_query_through()
    {
        TheSession.Store(new Something { Id = "4", Message = "Does this work?" });
        await TheSession.SaveChangesAsync();

        var ids = new string[1] { "4" };

        var results = await TheSession.Query<Something>()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task can_query_through_deeper()
    {
        TheSession.Store(new Something { Id = "4", Message = "Does this work?", Child = new SomeChild{Id = "3"}});
        await TheSession.SaveChangesAsync();

        var ids = new string[1] { "3" };

        var results = await TheSession.Query<Something>()
            .Where(s => ids.Contains(s.Child.Id))
            .ToListAsync();

        results.Count.ShouldBe(1);
    }
}

public class Something
{
    public string Id { get; set; }
    public string Message { get; set; }

    public SomeChild Child { get; set; }
}

public class SomeChild
{
    public string Id { get; set; }
}
