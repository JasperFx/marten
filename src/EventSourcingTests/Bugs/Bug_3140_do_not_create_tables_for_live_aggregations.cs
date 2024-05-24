using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3140_do_not_create_tables_for_live_aggregations : BugIntegrationContext
{
    [Fact]
    public async Task should_not_create_tables_for_live_aggregations_when_registered_directly()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new MyAggregateProjection(), ProjectionLifecycle.Live);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var existingTables = await theStore.Storage.Database.SchemaTables();
        existingTables.Any(x => x.QualifiedName == "bugs.mt_doc_aggregate").ShouldBeFalse();

        var tables = theStore.Storage.AllObjects().OfType<DocumentTable>();
        tables.ShouldNotContain(x => x.DocumentType == typeof(MyAggregate));
    }
}

public class MyAggregateProjection: SingleStreamProjection<MyAggregate>
{
    public void Apply(MyAggregate aggregate, AEvent e) => aggregate.ACount++;
}
