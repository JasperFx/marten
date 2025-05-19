using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class UnitOfWork_Operation_Ordering_Tests: OneOffConfigurationsContext, IAsyncLifetime
{
    private readonly Company _company;

    private readonly User _userNoIssues;
    private readonly User _userWithIssues;

    private readonly Issue _user1Issue2;
    private readonly Issue _user1Issue1;

    private readonly int _existingCompanyCount = 1;
    private readonly int _existingUserCount = 2;
    private readonly int _existingIssueCount = 2;

    public UnitOfWork_Operation_Ordering_Tests()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Company>();

            _.Schema.For<User>()
                .AddSubClass<AdminUser>();

            _.Schema.For<Issue>()
                .AddSubClass<CriticalIssue>()
                .ForeignKey<User>(u => u.AssigneeId);
        });

        _company = new Company();

        _userNoIssues = new User();

        _userWithIssues = new User();
        _user1Issue1 = new Issue { AssigneeId = _userWithIssues.Id };
        _user1Issue2 = new Issue { AssigneeId = _userWithIssues.Id };

    }

    public async Task InitializeAsync()
    {
        using var session = theStore.LightweightSession("Bug_1229");
        session.Store(_company);
        session.Store(_userNoIssues, _userWithIssues);
        session.Store(_user1Issue1, _user1Issue2);

        await session.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task unrelated_inserts_ordered_correctly()
    {
        await RunTest(s =>
            {
                s.Insert(new Company());
                s.Insert(new User());
                s.Insert(new Issue { AssigneeId = _userWithIssues.Id });
            },
            expectedCompanyCount: _existingCompanyCount + 1,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task unrelated_updates_ordered_correctly()
    {
        await RunTest(s =>
            {
                _company.Name = "Something else";
                _user1Issue1.Tags = new[] { "new tag" };
                _userNoIssues.FirstName = "A different name";

                s.Update(_company);
                s.Update(_user1Issue1);
                s.Update(_userNoIssues);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount,
            expectedIssueCount: _existingIssueCount
        );
    }

    [Fact]
    public async Task related_inserts_ordered_correctly()
    {
        await RunTest(s =>
            {
                var newUser = new User();

                s.Insert(newUser);
                s.Insert(new Issue { AssigneeId = newUser.Id });
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task related_upserts_ordered_correctly()
    {
        await RunTest(s =>
            {
                var newUser = new User();

                s.Store(newUser);
                s.Store(new Issue { AssigneeId = newUser.Id });
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task related_inserts_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                var newUser = new User();

                s.Insert(new Issue { AssigneeId = newUser.Id });
                s.Insert(newUser);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task related_inserts_on_subclass_fk_in_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                var newUser = new AdminUser();

                s.Insert(new Issue { AssigneeId = newUser.Id });
                s.Insert(newUser);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task related_inserts_on_subclass_fk_out_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                var newUser = new AdminUser();

                s.Insert(new CriticalIssue { AssigneeId = newUser.Id });
                s.Insert(newUser);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1,
            expectedIssueCount: _existingIssueCount + 1
        );
    }

    [Fact]
    public async Task unrelated_deletes()
    {
        await RunTest(s =>
            {
                s.Delete(_company);
                s.Delete(_userNoIssues);
                s.Delete(_user1Issue1);
            },
            expectedCompanyCount: _existingCompanyCount - 1,
            expectedUserCount: _existingUserCount - 1,
            expectedIssueCount: _existingIssueCount - 1
        );
    }

    [Fact]
    public async Task related_deletes_ordered_correctly()
    {
        await RunTest(s =>
            {
                s.Delete(_company);
                s.Delete(_user1Issue1);
                s.Delete(_user1Issue2);
                s.Delete(_userWithIssues);
            },
            expectedCompanyCount: _existingCompanyCount - 1,
            expectedUserCount: _existingUserCount - 1,
            expectedIssueCount: _existingIssueCount - 2
        );
    }

    [Fact]
    public async Task related_deletes_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                s.Delete(_company);
                s.Delete(_user1Issue1);
                s.Delete(_userWithIssues);
                s.Delete(_user1Issue2);
            },
            expectedCompanyCount: _existingCompanyCount - 1,
            expectedUserCount: _existingUserCount - 1,
            expectedIssueCount: _existingIssueCount - 2
        );
    }

    [Fact]
    public async Task related_deletes_and_unrelated_inserts_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                s.Insert(new Company());
                s.Insert(new User());

                s.Delete(_user1Issue1);
                s.Delete(_userWithIssues);
                s.Delete(_user1Issue2);
            },
            expectedCompanyCount: _existingCompanyCount + 1,
            expectedUserCount: _existingUserCount + 1 - 1,
            expectedIssueCount: _existingIssueCount - 2
        );
    }

    [Fact]
    public async Task related_deletes_and_unrelated_inserts_ordered_correctly()
    {
        await RunTest(s =>
            {
                s.Insert(new Company());
                s.Insert(new User());

                s.Delete(_user1Issue1);
                s.Delete(_user1Issue2);
                s.Delete(_userWithIssues);
            },
            expectedCompanyCount: _existingCompanyCount + 1,
            expectedUserCount: _existingUserCount + 1 - 1,
            expectedIssueCount: _existingIssueCount - 2
        );
    }

    [Fact]
    public async Task related_deletes_and_related_inserts_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                var newUser = new User();

                s.Insert(new Issue { AssigneeId = newUser.Id });

                s.Delete(_user1Issue1);
                s.Delete(_userWithIssues);
                s.Delete(_user1Issue2);

                s.Insert(newUser);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1 - 1,
            expectedIssueCount: _existingIssueCount + 1 - 2
        );
    }

    [Fact]
    public async Task related_deletes_and_related_upserts_ordered_incorrectly()
    {
        await RunTest(s =>
            {
                var newUser = new User();

                s.Store(new Issue { AssigneeId = newUser.Id });

                s.Delete(_user1Issue1);
                s.Delete(_userWithIssues);
                s.Delete(_user1Issue2);

                s.Store(newUser);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount + 1 - 1,
            expectedIssueCount: _existingIssueCount + 1 - 2
        );
    }

    [Fact]
    public async Task upsert_followed_by_delete_should_order_correctly()
    {
        await RunTest(s =>
            {
                var newUser = new User();
                s.Store(newUser);
                s.DeleteWhere<User>(x => x.Id == newUser.Id);
            },
            expectedCompanyCount: _existingCompanyCount,
            expectedUserCount: _existingUserCount,
            expectedIssueCount: _existingIssueCount
        );
    }

    private async Task RunTest(
        Action<IDocumentSession> act,
        int expectedCompanyCount,
        int expectedUserCount,
        int expectedIssueCount)
    {
        using (var s = theStore.LightweightSession("Bug_1229"))
        {
            act(s);

            await s.SaveChangesAsync();
        }

        using (var s = theStore.QuerySession("Bug_1229"))
        {
            var companies = s.Query<Company>().ToList();
            var users = s.Query<User>().ToList();
            var issues = s.Query<Issue>().ToList();

            companies.Count.ShouldBe(expectedCompanyCount);
            users.Count.ShouldBe(expectedUserCount);
            issues.Count.ShouldBe(expectedIssueCount);
        }
    }
}
