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

    }
}