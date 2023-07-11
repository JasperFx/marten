using System;
using System.Linq;
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
    public void correctly_use_include_when_not_deleted()
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
            session.SaveChanges();
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
    public void include_finds_nothing_when_it_is_soft_deleted()
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
            session.SaveChanges();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Delete(user);
            session.SaveChanges();
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
