using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class querying_against_timespan : IntegrationContext
{
    public querying_against_timespan(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task select_to_time_span()
    {
        var span = TimeSpan.Parse("-20154.01:12:32");

        var targets = Target.GenerateRandomData(10).ToArray();
        var first = targets.First();
        await theStore.BulkInsertDocumentsAsync(targets);

        var results = await theSession.Query<Target>().Where(x => x.Id == first.Id).Select(x => x.HowLong)
            .ToListAsync();
        results.Single().ShouldBe(first.HowLong);
    }
}
