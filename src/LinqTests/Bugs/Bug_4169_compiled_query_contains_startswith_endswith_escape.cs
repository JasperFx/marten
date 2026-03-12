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

public class Bug_4169_compiled_query_contains_startswith_endswith_escape : BugIntegrationContext
{
    public record WildcardDocument(Guid Id, string Name);

    // Each compiled query type must be unique to guarantee fresh code generation

    public class ContainsWithPercent : ICompiledListQuery<WildcardDocument>
    {
        public string Name { get; set; }

        public ContainsWithPercent(string name) => Name = name;

        public Expression<Func<IMartenQueryable<WildcardDocument>, IEnumerable<WildcardDocument>>> QueryIs()
            => q => q.Where(x => x.Name.Contains(Name));
    }

    public class StartsWithWithPercent : ICompiledListQuery<WildcardDocument>
    {
        public string Name { get; set; }

        public StartsWithWithPercent(string name) => Name = name;

        public Expression<Func<IMartenQueryable<WildcardDocument>, IEnumerable<WildcardDocument>>> QueryIs()
            => q => q.Where(x => x.Name.StartsWith(Name));
    }

    public class EndsWithWithPercent : ICompiledListQuery<WildcardDocument>
    {
        public string Name { get; set; }

        public EndsWithWithPercent(string name) => Name = name;

        public Expression<Func<IMartenQueryable<WildcardDocument>, IEnumerable<WildcardDocument>>> QueryIs()
            => q => q.Where(x => x.Name.EndsWith(Name));
    }

    [Fact]
    public async Task compiled_contains_should_not_treat_percent_as_wildcard()
    {
        var match = new WildcardDocument(Guid.NewGuid(), "100% Complete");
        var noMatch = new WildcardDocument(Guid.NewGuid(), "100 Complete");
        theSession.Store(match);
        theSession.Store(noMatch);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new ContainsWithPercent("100%"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(match.Id);
    }

    [Fact]
    public async Task compiled_starts_with_should_not_treat_percent_as_wildcard()
    {
        var match = new WildcardDocument(Guid.NewGuid(), "100% of target");
        var noMatch = new WildcardDocument(Guid.NewGuid(), "100 of target");
        theSession.Store(match);
        theSession.Store(noMatch);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new StartsWithWithPercent("100%"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(match.Id);
    }

    [Fact]
    public async Task compiled_ends_with_should_not_treat_percent_as_wildcard()
    {
        var match = new WildcardDocument(Guid.NewGuid(), "score: 100%");
        var noMatch = new WildcardDocument(Guid.NewGuid(), "score: 100");
        theSession.Store(match);
        theSession.Store(noMatch);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new EndsWithWithPercent("100%"))).ToList();

        results.Count.ShouldBe(1);
        results[0].Id.ShouldBe(match.Id);
    }
}
