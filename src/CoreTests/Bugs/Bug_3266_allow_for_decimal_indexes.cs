using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3266_allow_for_decimal_indexes : BugIntegrationContext
{
    [Fact]
    public async Task do_not_rebuild_index()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Company3266>().Index(c => c.SomeDecimal);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<Company3266>().Index(c => c.SomeDecimal);
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}

public class Company3266
{
    public Guid Id { get; set; }
    public decimal SomeDecimal { get; set; }
}
