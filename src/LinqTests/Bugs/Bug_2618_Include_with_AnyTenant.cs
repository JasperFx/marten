using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2618_Include_with_AnyTenant : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2618_Include_with_AnyTenant(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task tenant_filter_should_apply_to_both_parent_and_child_documents()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();

        });

        var user1a = new User();
        var issue1a = new Issue { AssigneeId = user1a.Id, Tags = new[] { "DIY" }, Title = "Garage Door is busted" };

        await using (var session = theStore.LightweightSession("one"))
        {
            session.Store(user1a);
            session.Store(issue1a);
            await session.SaveChangesAsync();
        }

        var user2a = new User();
        var issue2a = new Issue { AssigneeId = user2a.Id, Tags = new[] { "DIY" }, Title = "Garage Door is busted" };


        await using (var session = theStore.LightweightSession("two"))
        {
            session.Store(user2a);
            session.Store(issue2a);
            await session.SaveChangesAsync();
        }

        theSession.Logger = new TestOutputMartenLogger(_output);

        var users = new List<User>();

        var issues = await theSession
            .Query<Issue>().Where(x => x.AnyTenant() && x.Tags.Contains("DIY"))
            .Include(x => x.AssigneeId, users)
            .ToListAsync();

        // Get all the issues here
        issues.ShouldContain(x => x.Id == issue1a.Id);
        issues.ShouldContain(x => x.Id == issue2a.Id);

        // Get all the users too
        users.ShouldContain(x => x.Id == user1a.Id);
        users.ShouldContain(x => x.Id == user2a.Id);
    }
}
