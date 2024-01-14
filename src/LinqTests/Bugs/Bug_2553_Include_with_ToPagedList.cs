using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Pagination;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2553_Include_with_ToPagedList: BugIntegrationContext
{
    public record Foo(Guid Id);

    public record Bar(Guid Id, Guid FooId);

    [Fact]
    public void query_should_return_included_docs()
    {
        var foos = Enumerable.Range(start: 0, count: 100).Select(i => new Foo(Guid.NewGuid())).ToArray();
        var bars = Enumerable.Range(start: 0, count: 100).Select(i => new Bar(Guid.NewGuid(), FooId: foos[i].Id));

        foreach (var foo in foos)
        {
            TheSession.Store(foo);
        }

        foreach (var bar in bars)
        {
            TheSession.Store(bar);
        }

        TheSession.SaveChanges();

        var includedFoos = new Dictionary<Guid, Foo>();

        var queriedBars = TheSession.Query<Bar>()
            .Include(bar => bar.FooId, dictionary: includedFoos)
            .ToPagedList(pageNumber: 1, pageSize: 4);

        foreach (var queriedBar in queriedBars)
        {
            includedFoos.ShouldContainKey(key: queriedBar.FooId);
        }
    }
}
