using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class compiled_query_problem_with_search_by_string_and_string_collections(DefaultStoreFixture fixture): IntegrationContext(fixture)
{
    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    public class IssuesByTitles: ICompiledListQuery<Issue>, IQueryPlanning
    {
        public required string[] Titles { get; set; }
        public required string Status { get; set; }

        public Expression<Func<IMartenQueryable<Issue>, IEnumerable<Issue>>> QueryIs()
        {
            return query => query.Where(x => x.Status == Status && x.Title.IsOneOf(Titles));
        }
        void IQueryPlanning.SetUniqueValuesForQueryPlanning()
        {
            Status = "status";
            Titles = ["title"];
        }
    }

    [Fact]
    public async Task can_search_isOneOf_strings_with_compiled_queries_and_query_planning()
    {
        var issue1 = new Issue { Title = "Issue1", Status = "Open" };
        var issue2 = new Issue { Title = "Issue2", Status = "Open"};
        var issue3 = new Issue { Title = "Issue3", Status = "Open" };

        theSession.Store(issue1, issue2, issue3);
        await theSession.SaveChangesAsync();

        await using var session = theStore.QuerySession();
        var query = new IssuesByTitles { Titles = [issue1.Title, issue2.Title], Status = issue1.Status };
        var issues = await session.QueryAsync(query);

        issues.Count().ShouldBe(2);
    }
}
