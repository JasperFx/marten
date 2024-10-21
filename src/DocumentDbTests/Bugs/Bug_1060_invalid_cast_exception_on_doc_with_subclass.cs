using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1060_invalid_cast_exception_on_doc_with_subclass: BugIntegrationContext
{
    [Fact]
    public async Task can_issue_query_on_doc_hierarchy_with_include()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<User>()
                .AddSubClass<SuperUser>()
                .AddSubClass<AdminUser>();
            opts.Schema.For<Issue>();
        });

        var user = new User { Id = System.Guid.NewGuid() };
        var admin = new AdminUser { Id = System.Guid.NewGuid() };
        var issue = new Issue { Id = System.Guid.NewGuid(), ReporterId = user.Id };
        var issue2 = new Issue { Id = System.Guid.NewGuid(), ReporterId = admin.Id };

        await using var session = theStore.LightweightSession();
        session.Store(user);
        session.Store(admin);
        session.Store(issue);
        session.Store(issue2);
        await session.SaveChangesAsync();

        var users = new List<User>();
        var admins = new List<AdminUser>();

        var userIssues = session.Query<Issue>()
            .Include(i => i.ReporterId, users)
            .ToList();

        var adminIssues = session.Query<Issue>()
            .Include(i => i.ReporterId, admins)
            .ToList();

        // validate for parent document (base class)
        users.Count(p => p != null).ShouldBe(2);

        // validate for subclass document
        admins.Count(p => p != null).ShouldBe(1);
    }

}
