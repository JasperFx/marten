using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Util;
using Octokit;
using Shouldly;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;
using User = Marten.Testing.Documents.User;

namespace Marten.Testing.Services.Includes
{
    public class end_to_end_query_with_compiled_include_Tests : DocumentSessionFixture<IdentityMap>
    {
        // SAMPLE: compiled_include
        [Fact]
        public void simple_compiled_include_for_a_single_document()
        {
            var user = new User();
            var issue = new Issue { AssigneeId = user.Id, Title = "Garage Door is busted" };

            theSession.Store<object>(user, issue);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var issueQuery = new IssueByTitleWithAssignee {Title = issue.Title};
                var issue2 = query.Query(issueQuery);

                issueQuery.Included.ShouldNotBeNull();
                issueQuery.Included.Id.ShouldBe(user.Id);

                issue2.ShouldNotBeNull();
            }
        }

        public class IssueByTitleWithAssignee : ICompiledQuery<Issue>
        {
            public string Title { get; set; }
            public User Included { get; private set; } = new User();
            public JoinType JoinType { get; set; } = JoinType.Inner;

            public Expression<Func<IQueryable<Issue>, Issue>> QueryIs()
            {
                return query => query
                    .Include<Issue, IssueByTitleWithAssignee>(x => x.AssigneeId, x => x.Included, JoinType)
                    .Single(x => x.Title == Title);
            }
        }
        // ENDSAMPLE

        [Fact]
        public void compiled_query_with_multi_includes()
        {
            var user = new User();
            var reporter = new User();
            var issue = new Issue { AssigneeId = user.Id, ReporterId = reporter.Id, Title = "Garage Door is busted" };

            theSession.Store<object>(user, reporter, issue);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var issueQuery = new IssueByTitleIncludingUsers {Title = issue.Title};
                var issue2 = query.Query(issueQuery);

                issueQuery.IncludedAssignee.ShouldNotBeNull();
                issueQuery.IncludedAssignee.Id.ShouldBe(user.Id);
                issueQuery.IncludedReported.ShouldNotBeNull();
                issueQuery.IncludedReported.Id.ShouldBe(reporter.Id);

                issue2.ShouldNotBeNull();
            }
        }

public class IssueByTitleIncludingUsers : ICompiledQuery<Issue>
{
    public string Title { get; set; }
    public User IncludedAssignee { get; private set; } = new User();
    public User IncludedReported { get; private set; } = new User();
    public JoinType JoinType { get; set; } = JoinType.Inner;

    public Expression<Func<IQueryable<Issue>, Issue>> QueryIs()
    {
        return query => query
            .Include<Issue, IssueByTitleIncludingUsers>(x => x.AssigneeId, x => x.IncludedAssignee, JoinType)
            .Include<Issue, IssueByTitleIncludingUsers>(x => x.ReporterId, x => x.IncludedReported, JoinType)
            .Single(x => x.Title == Title);
    }
}

        // SAMPLE: compiled_include_list
        public class IssueWithUsers : ICompiledListQuery<Issue>
        {
            public List<User> Users { get; set; }
            // Can also work like that:
            //public List<User> Users => new List<User>();

            public Expression<Func<IQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
            {
                return query => query.Include<Issue, IssueWithUsers>(x => x.AssigneeId, x => x.Users, JoinType.Inner);
            }
        }

        [Fact]
        public void compiled_include_to_list()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3);
            theSession.SaveChanges();

            using (var session = theStore.QuerySession())
            {
                var query = new IssueWithUsers();

                var issues = session.Query(query).ToArray();

                query.Users.Count.ShouldBe(2);
                issues.Count().ShouldBe(3);

                query.Users.Any(x => x.Id == user1.Id);
                query.Users.Any(x => x.Id == user2.Id);
            }
        }
        // ENDSAMPLE

        // SAMPLE: compiled_include_dictionary
        public class IssueWithUsersById : ICompiledListQuery<Issue>
        {
            public IDictionary<Guid,User> UsersById { get; set; }
            // Can also work like that:
            //public List<User> Users => new Dictionary<Guid,User>();

            public Expression<Func<IQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
            {
                return query => query.Include<Issue, IssueWithUsersById>(x => x.AssigneeId, x => x.UsersById, JoinType.Inner);
            }
        }

        [Fact]
        public void compiled_include_to_dictionary()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3);
            theSession.SaveChanges();

            using (var session = theStore.QuerySession())
            {
                var query = new IssueWithUsersById();

                var issues = session.Query(query).ToArray();

                issues.ShouldNotBeEmpty();

                query.UsersById.Count.ShouldBe(2);
                query.UsersById.ContainsKey(user1.Id).ShouldBeTrue();
                query.UsersById.ContainsKey(user2.Id).ShouldBeTrue();
            }
        }
        // ENDSAMPLE
    }
}