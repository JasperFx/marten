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

// #5920: a compiled Count()/LongCount() after Skip()/Take() counts the page, not the whole table. Each
// compiled query runs against the same seeded documents as a LINQ to Objects reference, with several paging
// values per query type so the bound Skip/Take parameters are exercised and not just the first call's values.
public class compiled_count_over_paged_queries: IntegrationContext
{
    private static readonly PagedTarget[] Targets =
        Enumerable.Range(1, 10).Select(i => new PagedTarget { Id = Guid.NewGuid(), Number = i }).ToArray();

    public compiled_count_over_paged_queries(DefaultStoreFixture fixture): base(fixture) { }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedTarget)];

    protected override async Task fixtureSetup()
    {
        await theStore.BulkInsertDocumentsAsync(Targets);
    }

    [Fact]
    public void source_gen_path_is_engaged_for_the_count_shapes()
    {
        CompiledQueryHandlerRegistry.TryGet(typeof(TargetCountPageShape), out _).ShouldBeTrue();
        CompiledQueryHandlerRegistry.TryGet(typeof(TargetLongCountTakeShape), out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(3, 5, 4)]
    [InlineData(3, 0, 25)]
    [InlineData(3, 7, 3)]
    [InlineData(3, 12, 3)]
    [InlineData(0, 4, 0)]
    public async Task skip_take_count_matches_linq_to_objects(int minimum, int skip, int take)
    {
        var expected = Targets.Where(x => x.Number > minimum).OrderBy(x => x.Number).Skip(skip).Take(take).Count();

        var actual = await theSession.QueryAsync(
            new TargetCountPageShape { MinimumNumber = minimum, Skip = skip, Take = take },
            TestContext.Current.CancellationToken);

        actual.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(25)]
    public async Task take_long_count_matches_linq_to_objects(int take)
    {
        var actual = await theSession.QueryAsync(new TargetLongCountTakeShape { Take = take },
            TestContext.Current.CancellationToken);

        actual.ShouldBe(Targets.Take(take).LongCount());
    }
}
