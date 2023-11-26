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
        theSession.Store(new Something { Id = "4", Message = "Does this work?" });
        await theSession.SaveChangesAsync();

        var ids = new string[1] { "4" };

        var results = await theSession.Query<Something>()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();

        results.Count.ShouldBe(1);
    }
}

public class Something
{
    public string Id { get; set; }
    public string Message { get; set; }
}
