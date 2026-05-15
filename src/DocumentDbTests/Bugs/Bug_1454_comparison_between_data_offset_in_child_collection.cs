using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using System.Threading.Tasks;
using Marten;

namespace DocumentDbTests.Bugs;

public class Bug_1454_comparison_between_data_offset_in_child_collection : IntegrationContext
{
    public Bug_1454_comparison_between_data_offset_in_child_collection(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task can_query_against_DateTime()
    {
        (await theSession.Query<Target>().SelectMany(x => x.Children)
            .Where(x => x.Date == DateTime.Today.AddDays(-3))
            .ToListAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task can_query_against_DateTimeOffset()
    {
        (await theSession.Query<Target>().SelectMany(x => x.Children)
            .Where(x => x.DateOffset == DateTimeOffset.UtcNow)
            .ToListAsync()).ShouldNotBeNull();
    }
}
