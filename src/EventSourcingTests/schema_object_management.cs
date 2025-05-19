using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.Projections;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests;

public class schema_object_management : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_build_schema_with_auto_create_none()
    {
        var id = Guid.NewGuid();

        using (var store1 = DocumentStore.For(opts =>
               {
                   opts.Connection(ConnectionSource.ConnectionString);
                   opts.DatabaseSchemaName = "samples";
               }))
        {
            using (var session = store1.LightweightSession())
            {
                session.Events.StartStream<Quest>(id, new QuestStarted { Name = "Destroy the Orb" },
                    new MonsterSlayed { Name = "Troll" }, new MonsterSlayed { Name = "Dragon" });
                await session.SaveChangesAsync();
            }
        }

        #region sample_registering-event-types
        var store2 = DocumentStore.For(_ =>
        {
            _.DatabaseSchemaName = "samples";
            _.Connection(ConnectionSource.ConnectionString);
            _.AutoCreateSchemaObjects = AutoCreate.None;

            _.Events.AddEventType(typeof(QuestStarted));
            _.Events.AddEventType(typeof(MonsterSlayed));
        });
        #endregion

        using (var session = store2.LightweightSession())
        {
            (await session.Events.FetchStreamAsync(id)).Count.ShouldBe(3);
        }

        store2.Dispose();
    }

    [Fact]
    public async Task not_using_the_event_store_should_not_be_in_patch()
    {
        StoreOptions(_ => _.Schema.For<User>());

        var patch = await theStore.Storage.Database.CreateMigrationAsync();

        patch.UpdateSql().ShouldNotContain("mt_events");
        patch.UpdateSql().ShouldNotContain("mt_streams");
    }

    [Fact]
    public async Task do_not_create_tables()
    {
        StoreOptions(opts =>
        {
            opts.Projections.LiveStreamAggregation<QuestParty>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tables = theStore.Storage.AllObjects().OfType<Table>();
        tables.ShouldNotContain(x => x.Identifier.Name.Contains(nameof(QuestParty), StringComparison.OrdinalIgnoreCase));
    }

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

public class MyAggregateProjection: SingleStreamProjection<MyAggregate, Guid>
{
    public void Apply(MyAggregate aggregate, AEvent e) => aggregate.ACount++;
}
