using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;
using Issue = Marten.Testing.Documents.Issue;
using User = Marten.Testing.Documents.User;

namespace LinqTests.Includes;

public class end_to_end_query_with_compiled_include_Tests: IntegrationContext
{
    private readonly ITestOutputHelper _output;

    #region sample_compiled_include

    [Fact]
    public async Task simple_compiled_include_for_a_single_document()
    {
        var user = new User();
        var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

        using var session = theStore.IdentitySession();
        session.Store<object>(user, issue);
        await session.SaveChangesAsync();

        using var query = theStore.QuerySession();
        var issueQuery = new IssueByTitleWithAssignee { Title = issue.Title };
        var issue2 = query.Query(issueQuery);

        issueQuery.Included.ShouldNotBeNull();
        issueQuery.Included.Single().Id.ShouldBe(user.Id);

        issue2.ShouldNotBeNull();
    }

    public class IssueByTitleWithAssignee: ICompiledQuery<Issue>
    {
        public string Title { get; set; }
        public IList<User> Included { get; private set; } = new List<User>();

        public Expression<Func<IMartenQueryable<Issue>, Issue>> QueryIs()
        {
            return query => query
                .Include(x => x.AssigneeId, Included)
                .Single(x => x.Title == Title);
        }
    }

    #endregion

    #region sample_compiled_include_list

    public class IssueWithUsers: ICompiledListQuery<Issue>
    {
        public List<User> Users { get; set; } = new List<User>();
        // Can also work like that:
        //public List<User> Users => new List<User>();

        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return query => query.Include(x => x.AssigneeId, Users);
        }
    }

    [Fact]
    public async Task compiled_include_to_list()
    {
        var user1 = new User();
        var user2 = new User();

        var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
        var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
        var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

        using var session = theStore.IdentitySession();
        session.Store(user1, user2);
        session.Store(issue1, issue2, issue3);
        await session.SaveChangesAsync();

        using var querySession = theStore.QuerySession();
        var compiledQuery = new IssueWithUsers();

        querySession.Logger = new TestOutputMartenLogger(_output);
        var issues = querySession.Query(compiledQuery).ToArray();

        compiledQuery.Users.Count.ShouldBe(2);
        issues.Count().ShouldBe(3);

        compiledQuery.Users.Any(x => x.Id == user1.Id);
        compiledQuery.Users.Any(x => x.Id == user2.Id);
    }

    #endregion

    #region sample_compiled_include_dictionary

    public class IssueWithUsersById: ICompiledListQuery<Issue>
    {
        public IDictionary<Guid, User> UsersById { get; set; } = new Dictionary<Guid, User>();
        // Can also work like that:
        //public List<User> Users => new Dictionary<Guid,User>();

        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return query => query.Include(x => x.AssigneeId, UsersById);
        }
    }

    [Fact]
    public async Task compiled_include_to_dictionary()
    {
        var user1 = new User();
        var user2 = new User();

        var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
        var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
        var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

        using var session = theStore.IdentitySession();
        session.Store(user1, user2);
        session.Store(issue1, issue2, issue3);
        await session.SaveChangesAsync();

        using var querySession = theStore.QuerySession();
        var compiledQuery = new IssueWithUsersById();

        var issues = querySession.Query(compiledQuery).ToArray();

        issues.ShouldNotBeEmpty();

        compiledQuery.UsersById.Count.ShouldBe(2);
        compiledQuery.UsersById.ContainsKey(user1.Id).ShouldBeTrue();
        compiledQuery.UsersById.ContainsKey(user2.Id).ShouldBeTrue();
    }

    #endregion

    public end_to_end_query_with_compiled_include_Tests(DefaultStoreFixture fixture, ITestOutputHelper output):
        base(fixture)
    {
        _output = output;
    }
}
