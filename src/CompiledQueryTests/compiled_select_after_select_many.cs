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

// #5933: a compiled Select() after SelectMany() projects each flattened row. Several values run through the same
// compiled query type, so the multiplier read by the projection and the Take() are bound per execution and not
// frozen at their first values.
public class compiled_select_after_select_many: IntegrationContext
{
    private static readonly PagedHolder[] Holders = new[] { 4, 9, 1, 7, 10, 3, 8, 2, 6, 5 }.Select(i => new PagedHolder
    {
        Id = Guid.NewGuid(),
        Number = i,
        Items = Enumerable.Range(0, i % 4).Select(c => new PagedHolderItem { Holder = i, Index = c }).ToList()
    }).ToArray();

    public compiled_select_after_select_many(DefaultStoreFixture fixture): base(fixture) { }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedHolder)];

    protected override async Task fixtureSetup()
    {
        await theStore.BulkInsertDocumentsAsync(Holders);
    }

    [Fact]
    public void source_gen_path_is_engaged_for_the_shape()
    {
        CompiledQueryHandlerRegistry.TryGet(typeof(PagedHolderItemScoresShape), out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(100, 5)]
    [InlineData(3, 4)]
    [InlineData(7, 20)]
    [InlineData(1000, 1)]
    public async Task projection_matches_linq_to_objects(int multiplier, int take)
    {
        var expected = Holders.OrderBy(x => x.Number).SelectMany(x => x.Items)
            .Select(i => i.Index * multiplier + i.Holder).Take(take).ToList();

        var actual = await theSession.QueryAsync(new PagedHolderItemScoresShape { Multiplier = multiplier, Take = take },
            TestContext.Current.CancellationToken);

        actual.ToList().ShouldBe(expected);
    }
}
