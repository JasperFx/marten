using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class AsyncEnumerable : IntegrationContext
{
    public AsyncEnumerable(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    #region sample_query_to_async_enumerable

    [Fact]
    public async Task query_to_async_enumerable()
    {
        var targets = Target.GenerateRandomData(20).ToArray();
        await theStore.BulkInsertAsync(targets);

        var ids = new List<Guid>();

        var results = theSession.Query<Target>()
            .ToAsyncEnumerable();

        await foreach (var target in results)
        {
            ids.Add(target.Id);
        }

        ids.Count.ShouldBe(20);
        foreach (var target in targets)
        {
            ids.ShouldContain(target.Id);
        }
    }

    #endregion
}
