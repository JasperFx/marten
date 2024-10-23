using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2917_index_on_DateOnly : BugIntegrationContext
{
    [Fact]
    public async Task do_not_rebuild_index_for_dates()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DateHolder>().Index(x => x.Date);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<DateHolder>().Index(x => x.Date);
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task do_not_rebuild_index_for_times()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<TimeHolder>().Index(x => x.Time);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<TimeHolder>().Index(x => x.Time);
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}

public class DateHolder
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
}

public class TimeHolder
{
    public Guid Id { get; set; }
    public TimeOnly Time { get; set; }
}
