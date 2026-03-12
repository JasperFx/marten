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

public class Bug_4169_compiled_query_startswith_endswith_swap : BugIntegrationContext
{
    public record SwapDocument(Guid Id, string Name);

    // Select a scalar value to force StatelessCompiledQuery (no document selector)

    public class StartsWithProjection : ICompiledListQuery<SwapDocument, string>
    {
        public string Prefix { get; set; }

        public StartsWithProjection(string prefix) => Prefix = prefix;

        public Expression<Func<IMartenQueryable<SwapDocument>, IEnumerable<string>>> QueryIs()
            => q => q.Where(x => x.Name.StartsWith(Prefix)).Select(x => x.Name);
    }

    public class EndsWithProjection : ICompiledListQuery<SwapDocument, string>
    {
        public string Suffix { get; set; }

        public EndsWithProjection(string suffix) => Suffix = suffix;

        public Expression<Func<IMartenQueryable<SwapDocument>, IEnumerable<string>>> QueryIs()
            => q => q.Where(x => x.Name.EndsWith(Suffix)).Select(x => x.Name);
    }

    [Fact]
    public async Task compiled_starts_with_projection_should_match_prefix_not_suffix()
    {
        var match = new SwapDocument(Guid.NewGuid(), "hello world");
        var noMatch = new SwapDocument(Guid.NewGuid(), "world hello");
        theSession.Store(match);
        theSession.Store(noMatch);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new StartsWithProjection("hello"))).ToList();

        results.Count.ShouldBe(1);
        results[0].ShouldBe("hello world");
    }

    [Fact]
    public async Task compiled_ends_with_projection_should_match_suffix_not_prefix()
    {
        var match = new SwapDocument(Guid.NewGuid(), "world hello");
        var noMatch = new SwapDocument(Guid.NewGuid(), "hello world");
        theSession.Store(match);
        theSession.Store(noMatch);
        await theSession.SaveChangesAsync();

        var results = (await theSession.QueryAsync(new EndsWithProjection("hello"))).ToList();

        results.Count.ShouldBe(1);
        results[0].ShouldBe("world hello");
    }
}
