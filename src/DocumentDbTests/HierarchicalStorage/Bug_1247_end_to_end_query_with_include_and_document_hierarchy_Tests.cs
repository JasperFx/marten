using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.HierarchicalStorage;

public class Bug_1247_query_with_include_and_document_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
{
    private new readonly ITestOutputHelper _output;

    public Bug_1247_query_with_include_and_document_hierarchy_Tests(ITestOutputHelper output)
    {
        _output = output;
    }

    // [Fact] flaky in CI
    public void include_to_list_using_outer_join()
    {
        var user1 = new User();
        var user2 = new User();

        var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted1" };
        var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted2" };
        var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted3" };
        var issue4 = new Issue { AssigneeId = null, Title = "Garage Door is busted4" };

        using var session = TheStore.IdentitySession();
        session.Store(user1, user2);
        session.Store(issue1, issue2, issue3, issue4);
        session.SaveChanges();

        using var query = TheStore.QuerySession();
        query.Logger = new TestOutputMartenLogger(_output);

        var list = new List<User>();

        var issues = query.Query<Issue>().Include<User>(x => x.AssigneeId, list).ToArray();

        list.Count.ShouldBe(2);

        list.Any(x => x.Id == user1.Id).ShouldBeTrue();
        list.Any(x => x.Id == user2.Id).ShouldBeTrue();

        issues.Length.ShouldBe(4);
    }

    // [Fact] flaky in CI
    public async Task include_to_list_using_outer_join_async()
    {
        var user1 = new User();
        var user2 = new User();

        var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted1" };
        var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted2" };
        var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted3" };
        var issue4 = new Issue { AssigneeId = null, Title = "Garage Door is busted4" };

        await using var session = TheStore.IdentitySession();
        session.Store(user1, user2);
        session.Store(issue1, issue2, issue3, issue4);
        await session.SaveChangesAsync();

        await using var query = TheStore.QuerySession();
        var list = new List<User>();

        var issues = await query.Query<Issue>().Include<User>(x => x.AssigneeId, list).ToListAsync();

        list.Count.ShouldBe(2);

        list.Any(x => x.Id == user1.Id).ShouldBeTrue();
        list.Any(x => x.Id == user2.Id).ShouldBeTrue();

        issues.Count.ShouldBe(4);
    }
}
