using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_3096_include_where_select : IntegrationContext
{
    public Bug_3096_include_where_select(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task include_to_dictionary_with_where_and_projection()
    {
        var user1 = new User();
        var user2 = new User();

        var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is ok" };
        var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
        var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

        using var session = theStore.IdentitySession();
        session.Store(user1, user2);
        session.Store(issue1, issue2, issue3);
        await session.SaveChangesAsync();

        using var query = theStore.QuerySession();
        var dict = new Dictionary<Guid, User>();

        var issues = query
            .Query<Issue>()
            .Where(i => i.Title.Contains("ok"))
            .Include(x => x.AssigneeId, dict)
            .Select(i => new { i.Id, i.Title, })
            .ToArray();

        issues.Length.ShouldBe(1);

        dict.Count.ShouldBe(1);
        dict.ContainsKey(user1.Id).ShouldBeTrue();
    }
}
