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

// #5942: a compiled, computed top-level Select(). Several values run through the same compiled query type, so the
// multiplier read by the projection and the Take() are bound per execution and not frozen at their first values.
public class compiled_computed_select: IntegrationContext
{
    private static readonly PagedHolder[] Holders = new[] { 4, 9, 1, 7, 10, 3, 8, 2, 6, 5 }
        .Select(i => new PagedHolder { Id = Guid.NewGuid(), Number = i }).ToArray();

    public compiled_computed_select(DefaultStoreFixture fixture): base(fixture) { }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedHolder)];

    protected override async Task fixtureSetup()
    {
        await theStore.BulkInsertDocumentsAsync(Holders);
    }

    [Fact]
    public void source_gen_path_is_engaged_for_the_shape()
    {
        CompiledQueryHandlerRegistry.TryGet(typeof(PagedHolderNumberScoresShape), out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(5, 3)]
    [InlineData(11, 10)]
    [InlineData(2, 1)]
    public async Task projection_matches_linq_to_objects(int multiplier, int take)
    {
        var expected = Holders.OrderBy(x => x.Number).Select(x => x.Number * multiplier % 7).Take(take).ToList();

        var actual = await theSession.QueryAsync(new PagedHolderNumberScoresShape { Multiplier = multiplier, Take = take },
            TestContext.Current.CancellationToken);

        actual.ToList().ShouldBe(expected);
    }
}
