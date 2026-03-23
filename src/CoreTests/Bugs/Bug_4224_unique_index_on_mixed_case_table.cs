using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
/// Reproduces https://github.com/JasperFx/weasel/issues/224
/// A FlatTableProjection with a mixed-case table name and a unique index
/// should not fail on the second migration run with "relation already exists".
/// </summary>
public class Bug_4224_unique_index_on_mixed_case_table : BugIntegrationContext
{
    [Fact]
    public async Task should_not_fail_on_second_migration_with_unique_index()
    {
        // Matches the user's exact scenario from the issue
        StoreOptions(options =>
        {
            options.DatabaseSchemaName = "bug_4224_marten";
            options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

            options.Projections.Add<TestProjector>(ProjectionLifecycle.Inline);
        });

        // First migration - creates schema, table, and index
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Second migration - should detect existing index and not try to recreate it
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}

public class TestProjector : FlatTableProjection
{
    public TestProjector()
        : base(new DbObjectName("bug_4224", "TestRecords"))
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<Guid>("fake_id");
        Table.AddColumn<int>("year");
        Table.AddColumn<int>("month");

        Table.Indexes.Add(new IndexDefinition("ix_test_records_unique")
        {
            Columns = ["fake_id", "year", "month"],
            IsUnique = true
        });

        // Need at least one event handler for validation
        Project<TestEvent>(map =>
        {
            map.Map(x => x.FakeId, "fake_id");
            map.Map(x => x.Year, "year");
            map.Map(x => x.Month, "month");
        });
    }
}

public class TestEvent
{
    public Guid FakeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
}
