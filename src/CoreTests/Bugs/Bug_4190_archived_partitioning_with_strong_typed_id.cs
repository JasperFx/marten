using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_4190_archived_partitioning_with_strong_typed_id : BugIntegrationContext
{
    // Strongly-typed ID similar to Vogen-generated value types
    public record EntityId(Guid Value);

    [Fact]
    public void event_tag_table_fk_correction_should_not_throw_with_archived_partitioning()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<EntityId>("entity");
        });

        var events = theStore.Options.EventGraph;
        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)events).Objects;

        var tagTable = schemaObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name.Contains("mt_event_tag"));
        tagTable.ShouldNotBeNull();

        var eventsTable = schemaObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "mt_events");
        eventsTable.ShouldNotBeNull();

        // Verify the events table PK includes is_archived when partitioned
        eventsTable.PrimaryKeyColumns.ShouldContain("is_archived",
            "mt_events PK should include is_archived when UseArchivedStreamPartitioning is enabled");

        // The tag table should have is_archived column to satisfy the FK correction
        var isArchivedCol = tagTable.Columns.FirstOrDefault(c => c.Name == "is_archived");
        isArchivedCol.ShouldNotBeNull(
            "EventTagTable should have is_archived column when UseArchivedStreamPartitioning is enabled");

        // Explicitly test PostProcess doesn't throw
        Should.NotThrow(() => tagTable.PostProcess(schemaObjects));
    }

    [Fact]
    public async Task can_create_schema_with_archived_partitioning_and_tag_type()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<EntityId>("entity");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task schema_is_idempotent_with_archived_partitioning_and_tag_type()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<EntityId>("entity");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.RegisterTagType<EntityId>("entity");
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task can_create_schema_with_archived_partitioning_conjoined_tenancy_and_tag_type()
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined;
            opts.Events.RegisterTagType<EntityId>("entity");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
