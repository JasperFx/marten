using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_4169_compiled_query_ilike_escape : BugIntegrationContext
{
    public record IlikeEscapeDocument(Guid Id, string DisplayName);

    // Separate compiled query types per test to guarantee each triggers fresh code generation
    // with the specific special character value

    public class FindByDisplayNameWithPercent : ICompiledListQuery<IlikeEscapeDocument>
    {
        public string DisplayName { get; set; }

        public FindByDisplayNameWithPercent(string displayName)
        {
            DisplayName = displayName;
        }

        public Expression<Func<IMartenQueryable<IlikeEscapeDocument>, IEnumerable<IlikeEscapeDocument>>> QueryIs()
        {
            return q => q.Where(x => x.DisplayName.Equals(DisplayName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class FindByDisplayNameWithUnderscore : ICompiledListQuery<IlikeEscapeDocument>
    {
        public string DisplayName { get; set; }

        public FindByDisplayNameWithUnderscore(string displayName)
        {
            DisplayName = displayName;
        }

        public Expression<Func<IMartenQueryable<IlikeEscapeDocument>, IEnumerable<IlikeEscapeDocument>>> QueryIs()
        {
            return q => q.Where(x => x.DisplayName.Equals(DisplayName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class FindByDisplayNameWithBackslash : ICompiledListQuery<IlikeEscapeDocument>
    {
        public string DisplayName { get; set; }

        public FindByDisplayNameWithBackslash(string displayName)
        {
            DisplayName = displayName;
        }

        public Expression<Func<IMartenQueryable<IlikeEscapeDocument>, IEnumerable<IlikeEscapeDocument>>> QueryIs()
        {
            return q => q.Where(x => x.DisplayName.Equals(DisplayName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    [Fact]
    public async Task compiled_query_with_percentage_in_equals_ignore_case()
    {
        var doc = new IlikeEscapeDocument(Guid.NewGuid(), "100% Complete");
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new FindByDisplayNameWithPercent("100% Complete"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task compiled_query_with_underscore_in_equals_ignore_case()
    {
        var doc = new IlikeEscapeDocument(Guid.NewGuid(), "hello_world");
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new FindByDisplayNameWithUnderscore("hello_world"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task compiled_query_with_backslash_in_equals_ignore_case()
    {
        var doc = new IlikeEscapeDocument(Guid.NewGuid(), @"path\to\file");
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new FindByDisplayNameWithBackslash(@"path\to\file"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(doc.Id);
    }
}
