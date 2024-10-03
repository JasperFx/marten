using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Pagination;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2553_Include_with_ToPagedList: BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record Foo(Guid Id);

    public record Bar(Guid Id, Guid FooId);

    public Bug_2553_Include_with_ToPagedList(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task query_should_return_included_docs()
    {
        var foos = Enumerable.Range(start: 0, count: 100).Select(i => new Foo(Guid.NewGuid())).ToArray();
        var bars = Enumerable.Range(start: 0, count: 100).Select(i => new Bar(Guid.NewGuid(), FooId: foos[i].Id));

        foreach (var foo in foos)
        {
            theSession.Store(foo);
        }

        foreach (var bar in bars)
        {
            theSession.Store(bar);
        }

        await theSession.SaveChangesAsync();

        var includedFoos = new Dictionary<Guid, Foo>();

        theSession.Logger = new TestOutputMartenLogger(_output);
        var queriedBars = await theSession.Query<Bar>()
            .Include(bar => bar.FooId, dictionary: includedFoos)
            .ToPagedListAsync(pageNumber: 1, pageSize: 4);

        foreach (var queriedBar in queriedBars)
        {
            includedFoos.ShouldContainKey(key: queriedBar.FooId);
        }
    }
}
