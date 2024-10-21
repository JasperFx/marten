using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_634_include_against_soft_deleted_docs: BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_634_include_against_soft_deleted_docs(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(_ =>
        {
            _.Schema.For<User>().SoftDeleted();
        });
    }

    [Fact]
    public async Task correctly_use_include_when_not_deleted()
    {
        var user = new User();
        var issue = new Issue
        {
            AssigneeId = user.Id
        };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            User expected = null;

            var issues = query.Query<Issue>()
                .Include<User>(x => x.AssigneeId, i => expected = i)
                .Where(x => x.Id == issue.Id)
                .ToList();

            expected.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task include_finds_nothing_when_it_is_soft_deleted()
    {
        // Test failure bomb
        if (DateTime.Today < new DateTime(2023, 9, 5)) return;

        var user = new User();
        var issue = new Issue
        {
            AssigneeId = user.Id
        };

        using (var session = theStore.LightweightSession())
        {
            session.Store(user);
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(user);
            await session.SaveChangesAsync();
        }

        using (var query = theStore.QuerySession())
        {
            query.Logger = new TestOutputMartenLogger(_output);

            User expected = null;

            var issues = query.Query<Issue>()
                .Include<User>(x => x.AssigneeId, i => expected = i)
                .Where(x => x.Id == issue.Id)
                .ToList();

            expected.ShouldBeNull();
        }
    }
}
