using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance;

public class against_date_time_offset : IntegrationContext
{
    public against_date_time_offset(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task query_against_date_time_offset_that_is_not_universal_time()
    {
        await theStore.BulkInsertDocumentsAsync(Target.GenerateRandomData(1000).ToArray());

        var results = await theSession.Query<Target>().Where(x => x.DateOffset < DateTimeOffset.Now.AddDays(-1))
            .ToListAsync();
    }
}
