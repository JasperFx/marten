using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class compiled_query_problem_with_includes_and_ICompiledQuery_reuse: IntegrationContext
    {
        public compiled_query_problem_with_includes_and_ICompiledQuery_reuse(DefaultStoreFixture fixture): base(fixture)
        {
        }

        public class IssueWithUsers: ICompiledListQuery<Issue>
        {
            public List<User> Users { get; } = new List<User>();

            public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
            {
                return query => query.Include(x => x.AssigneeId, Users);
            }
        }

        public class IssueWithUsersAndParam: ICompiledListQuery<Issue>
        {
            public List<User> Users { get; set; } = new();
            public Guid UserId { get; set; }

            public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
            {
                return query => query
                    .Where(x => x.AssigneeId == UserId)
                    .Include(x => x.AssigneeId, Users);
            }
        }

        [Fact]
        public void can_get_includes_with_compiled_queries()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue {AssigneeId = user1.Id, Title = "Garage Door is busted"};
            var issue2 = new Issue {AssigneeId = user2.Id, Title = "Garage Door is busted"};
            var issue3 = new Issue {AssigneeId = user2.Id, Title = "Garage Door is busted"};

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3);
            theSession.SaveChanges();

            // Issue first query
            using (var session = theStore.QuerySession())
            {
                var query = new IssueWithUsers();
                var issues = session.Query(query).ToArray();

                query.Users.Count.ShouldBe(2);
                issues.Count().ShouldBe(3);

                query.Users.Any(x => x.Id == user1.Id).ShouldBeTrue();
                query.Users.Any(x => x.Id == user2.Id).ShouldBeTrue();
            }

            // Issue second query
            using (var session = theStore.QuerySession())
            {
                var query = new IssueWithUsers();
                var issues = session.Query(query).ToArray();

                // Should populate this instance of IssueWithUsers
                query.Users.ShouldNotBeNull();
                // Should not re-use a previous instance of IssueWithUsers, which would make the count 4
                query.Users.Count.ShouldBe(2);
                issues.Count().ShouldBe(3);

                query.Users.Any(x => x.Id == user1.Id);
                query.Users.Any(x => x.Id == user2.Id);
            }
        }

        [Fact]
        public void can_get_includes_with_multiple_instances_of_same_compiled_query_in_a_session()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue {AssigneeId = user1.Id, Title = "Garage Door is busted 1"};
            var issue2 = new Issue {AssigneeId = user2.Id, Title = "Garage Door is busted 2"};
            var issue3 = new Issue {AssigneeId = user2.Id, Title = "Garage Door is busted 3"};

            theSession.Store(user1, user2);
            theSession.Store(issue1, issue2, issue3);
            theSession.SaveChanges();

            using var session = theStore.QuerySession();

            var issuesWithUsersQry1 = new IssueWithUsersAndParam { UserId = user1.Id};
            var issues1 = session.Query(issuesWithUsersQry1).ToArray();
            issuesWithUsersQry1.Users.Count.ShouldBe(1);
            issues1.Count().ShouldBe(1);
            issuesWithUsersQry1.Users.Any(x => x.Id == user1.Id).ShouldBeTrue();
            issuesWithUsersQry1.Users.Any(x => x.Id == user2.Id).ShouldBeFalse();

            // query using another instance of compiled query `IssueWithUsers`
            var issuesWithUsersQry2 = new IssueWithUsersAndParam { UserId = user2.Id};
            var issues2 = session.Query(issuesWithUsersQry2).ToArray();
            issuesWithUsersQry2.Users.Count.ShouldBe(1);
            issues2.Count().ShouldBe(2);
            issuesWithUsersQry2.Users.Any(x => x.Id == user2.Id).ShouldBeTrue();
            issuesWithUsersQry2.Users.Any(x => x.Id == user1.Id).ShouldBeFalse();
        }
    }
}
