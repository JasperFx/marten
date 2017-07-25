using Marten.Linq;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Testing.Documents;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class compiled_query_problem_with_includes_and_ICompiledQuery_reuse : DocumentSessionFixture<IdentityMap>
    {
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
        public void can_get_includes_with_compiled_queries()
        {
            var user1 = new User();
            var user2 = new User();

            var issue1 = new Issue { AssigneeId = user1.Id, Title = "Garage Door is busted" };
            var issue2 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };
            var issue3 = new Issue { AssigneeId = user2.Id, Title = "Garage Door is busted" };

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

                query.Users.Any(x => x.Id == user1.Id);
                query.Users.Any(x => x.Id == user2.Id);
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
    }
}
