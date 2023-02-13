using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.BatchedQuerying;

public class batched_querying_with_aggregate_functions: IntegrationContext
{
    [Fact]
    public async Task can_run_aggregate_functions()
    {
        await using var session = theStore.IdentitySession();
        session.Store(new IntDoc(1), new IntDoc(3), new IntDoc(5), new IntDoc(6));
        await session.SaveChangesAsync();

        var batch = session.CreateBatchQuery();

        var min = batch.Query<IntDoc>().Min(x => x.Id);
        var max = batch.Query<IntDoc>().Max(x => x.Id);
        var sum = batch.Query<IntDoc>().Sum(x => x.Id);
        var average = batch.Query<IntDoc>().Average(x => x.Id);

        await batch.Execute();

        (await min).ShouldBe(1);
        (await max).ShouldBe(6);
        (await sum).ShouldBe(1 + 3 + 5 + 6);
        (await average).ShouldBe(3.75);
    }

    public batched_querying_with_aggregate_functions(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
