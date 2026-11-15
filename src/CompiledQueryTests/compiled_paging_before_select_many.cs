using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.CompiledQueries;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CompiledQueryTests;

// #5928: a compiled Skip()/Take() before SelectMany() pages the parent documents and returns every item of the
// documents on the page. Several paging values run through the same compiled query type, so the bound
// Skip/Take parameters are exercised and not just the first call's values.
public class compiled_paging_before_select_many: IntegrationContext
{
    // Items per holder cycle 1,2,3,0 by Number, stored out of Number order
    private static readonly PagedHolder[] Holders = new[] { 4, 9, 1, 7, 10, 3, 8, 2, 6, 5 }.Select(i => new PagedHolder
    {
        Id = Guid.NewGuid(),
        Number = i,
        Items = Enumerable.Range(0, i % 4).Select(c => new PagedHolderItem { Holder = i, Index = c }).ToList()
    }).ToArray();

    public compiled_paging_before_select_many(DefaultStoreFixture fixture): base(fixture) { }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedHolder)];

    protected override async Task fixtureSetup()
    {
        await theStore.BulkInsertDocumentsAsync(Holders);
    }

    [Fact]
    public void source_gen_path_is_engaged_for_the_shape()
    {
        CompiledQueryHandlerRegistry.TryGet(typeof(PagedHolderItemsShape), out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(2, 25)]
    [InlineData(12, 3)]
    [InlineData(3, 0)]
    public async Task skip_take_select_many_matches_linq_to_objects(int skip, int take)
    {
        var expected = Holders.OrderBy(x => x.Number).Skip(skip).Take(take).SelectMany(x => x.Items).ToList();

        var actual = await theSession.QueryAsync(new PagedHolderItemsShape { Skip = skip, Take = take },
            TestContext.Current.CancellationToken);

        actual.ToList().ShouldBe(expected);
    }
}
