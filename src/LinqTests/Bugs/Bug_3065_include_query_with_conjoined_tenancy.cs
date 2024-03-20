using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_3065_include_query_with_conjoined_tenancy : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3065_include_query_with_conjoined_tenancy(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task include_with_SingleOrDefault_where_for_a_single_document()
    {
        StoreOptions(opts => opts.Policies.AllDocumentsAreMultiTenanted());

        var user = new User{Id = Guid.NewGuid()};
        var issue = new Issue { AssigneeId = user.Id, Tags = new[] { "DIY" }, Title = "Garage Door is busted" };

        using (var session = theStore.LightweightSession("a"))
        {
            session.Store<object>(user, issue);
            await session.SaveChangesAsync();
        }

        using (var session2 = theStore.LightweightSession("b"))
        {
            var user2 = new User{Id = user.Id};
            var issue2 = new Issue { AssigneeId = user.Id, Tags = new[] { "DIY" }, Title = "Garage Door is busted" };

            session2.Store<object>(user2, issue2);
            await session2.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession("a");

        query.Logger = new TestOutputMartenLogger(_output);

        User included = null;
        var issueLoaded = await query
            .Query<Issue>()
            .Include<User>(x => x.AssigneeId, x => included = x)
            .SingleOrDefaultAsync(x => x.Id == issue.Id);

        included.ShouldNotBeNull();
        included.Id.ShouldBe(user.Id);

        issueLoaded.ShouldNotBeNull();
    }
}
