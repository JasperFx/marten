using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using Issue = Marten.Testing.Documents.Issue;
using User = Marten.Testing.Documents.User;

namespace Marten.Testing.Services.Includes
{
    public class end_to_end_query_with_compiled_include_Tests : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        #region sample_compiled_include
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

                SpecificationExtensions.ShouldNotBeNull(issueQuery.Included);
                issueQuery.Included.Single().Id.ShouldBe(user.Id);

                SpecificationExtensions.ShouldNotBeNull(issue2);
            }
        }

        public class IssueByTitleWithAssignee : ICompiledQuery<Issue>
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
        #endregion sample_compiled_include


        #region sample_compiled_include_list
        public class IssueWithUsers : ICompiledListQuery<Issue>
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
        #endregion sample_compiled_include_list

        #region sample_compiled_include_dictionary
        public class IssueWithUsersById : ICompiledListQuery<Issue>
        {
            public IDictionary<Guid,User> UsersById { get; set; } = new Dictionary<Guid, User>();
            // Can also work like that:
            //public List<User> Users => new Dictionary<Guid,User>();

            public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
            {
                return query => query.Include(x => x.AssigneeId, UsersById);
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
        #endregion sample_compiled_include_dictionary
        public end_to_end_query_with_compiled_include_Tests(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
            DocumentTracking = DocumentTracking.IdentityOnly;
        }
    }
}
