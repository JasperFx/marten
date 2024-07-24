using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Services.BatchQuerying;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading;

public class query_plans : IntegrationContext
{
    public query_plans(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task query_by_query_plan()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        var targets = Target.GenerateRandomData(1000).ToArray();
        await theStore.BulkInsertDocumentsAsync(targets);

        var blues = await theSession.QueryByPlanAsync(new ColorTargets(Colors.Blue));

        blues.ShouldNotBeEmpty();

        var expected = targets.Where(x => x.Color == Colors.Blue).OrderBy(x => x.Number);

        blues.Select(x => x.Id).ShouldBe(expected.Select(x => x.Id));
    }

    #region sample_using_query_plan_in_batch_query

    [Fact]
    public async Task use_as_batch()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        var targets = Target.GenerateRandomData(1000).ToArray();
        await theStore.BulkInsertDocumentsAsync(targets);

        // Start a batch query
        var batch = theSession.CreateBatchQuery();

        // Using the ColorTargets plan twice, once for "Blue" and once for "Green" target documents
        var blueFetcher = batch.QueryByPlan(new ColorTargets(Colors.Blue));
        var greenFetcher = batch.QueryByPlan(new ColorTargets(Colors.Green));

        // Execute the batch query
        await batch.Execute();

        // The batched querying in Marten is essentially registering a "future"
        // for each query, so we'll await each task from above to get at the actual
        // data returned from batch.Execute() above
        var blues = await blueFetcher;
        var greens = await greenFetcher;

        // And the assertion part of our arrange, act, assertion test
        blues.ShouldNotBeEmpty();
        greens.ShouldNotBeEmpty();

        var expectedBlues = targets.Where(x => x.Color == Colors.Blue).OrderBy(x => x.Number);
        var expectedReds = targets.Where(x => x.Color == Colors.Green).OrderBy(x => x.Number);

        blues.Select(x => x.Id).ShouldBe(expectedBlues.Select(x => x.Id));
        greens.Select(x => x.Id).ShouldBe(expectedReds.Select(x => x.Id));
    }

    #endregion
}

#region sample_color_targets

public class ColorTargets: QueryListPlan<Target>
{
    public Colors Color { get; }

    public ColorTargets(Colors color)
    {
        Color = color;
    }

    // All we're doing here is just turning around and querying against the session
    // All the same though, this approach lets you do much more runtime logic
    // than a compiled query can
    public override IQueryable<Target> Query(IQuerySession session)
    {
        return session.Query<Target>().Where(x => x.Color == Color).OrderBy(x => x.Number);
    }
}

// The above is short hand for:

public class LonghandColorTargets: IQueryPlan<IReadOnlyList<Target>>, IBatchQueryPlan<IReadOnlyList<Target>>
{
    public Colors Color { get; }

    public LonghandColorTargets(Colors color)
    {
        Color = color;
    }

    public Task<IReadOnlyList<Target>> Fetch(IQuerySession session, CancellationToken token)
    {
        return session
            .Query<Target>()
            .Where(x => x.Color == Color)
            .OrderBy(x => x.Number)
            .ToListAsync(token: token);
    }

    public Task<IReadOnlyList<Target>> Fetch(IBatchedQuery batch)
    {
        return batch
            .Query<Target>()
            .Where(x => x.Color == Color)
            .OrderBy(x => x.Number)
            .ToList();
    }
}

#endregion

public static class query_plan_samples
{
    #region sample_using_query_plan

    public static async Task use_query_plan(IQuerySession session, CancellationToken token)
    {
        var targets = await session
            .QueryByPlanAsync(new ColorTargets(Colors.Blue), token);
    }

    #endregion
}
