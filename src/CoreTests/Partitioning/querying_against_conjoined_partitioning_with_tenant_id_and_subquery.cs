using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Partitioning;

public class querying_against_conjoined_partitioning_with_tenant_id_and_subquery : OneOffConfigurationsContext
{
    [Fact]
    public async Task do_not_bleed_tenant_data_because_of_select_queries()
    {
        var reds = Target.GenerateRandomData(100).ToArray();
        var blues = Target.GenerateRandomData(1000).ToArray();

        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x =>
            {
                x.ByList()
                    .AddPartition("red", "red")
                    .AddPartition("blue", "blue");
            }, PrimaryKeyTenancyOrdering.TenantId_Then_Id);
        });

        await theStore.BulkInsertAsync("red", reds);
        await theStore.BulkInsertAsync("blue", blues);

        using var session = theStore.LightweightSession("red");

        var matching = await session.Query<Target>()
            .Where(x => x.Children.Any(c => c.Number > 8))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync();

        var expected = reds.Where(x => x.Children.Any(c => c.Number > 8))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToList();

        matching.Count.ShouldBe(expected.Count);

        matching.ShouldBe(expected);
    }
}
