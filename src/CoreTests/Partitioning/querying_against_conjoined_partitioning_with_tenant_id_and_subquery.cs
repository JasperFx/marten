using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests.Partitioning;

public class querying_against_conjoined_partitioning_with_tenant_id_and_subquery : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public querying_against_conjoined_partitioning_with_tenant_id_and_subquery(ITestOutputHelper output)
    {
        _output = output;
    }

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
            });
        });

        await theStore.BulkInsertAsync("red", reds);
        await theStore.BulkInsertAsync("blue", blues);

        using var session = theStore.LightweightSession("red");
        session.Logger = new TestOutputMartenLogger(_output);

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
