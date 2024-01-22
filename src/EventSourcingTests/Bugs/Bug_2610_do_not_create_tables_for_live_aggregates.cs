using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2610_do_not_create_tables_for_live_aggregates : BugIntegrationContext
{
    [Fact]
    public async Task do_not_create_tables()
    {
        StoreOptions(opts => opts.Projections.LiveStreamAggregation<QuestParty>());

        await TheStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tables = TheStore.Storage.AllObjects().OfType<Table>();
        tables.ShouldNotContain(x => x.Identifier.Name.Contains(nameof(QuestParty), StringComparison.OrdinalIgnoreCase));
    }
}
