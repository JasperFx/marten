using System.Linq;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_progression_keying.cs
/// — #4596 Phase 1 Session 3 schema-shape pin: with the per-tenant flag ON,
/// <c>mt_event_progression</c>'s primary key STAYS <c>(name)</c>. Per-tenant
/// rows are distinguished entirely inside <see cref="JasperFx.Events.Projections.ShardName.Identity"/>
/// (the tenant id is folded into the <c>name</c> column suffix). No tenant_id
/// column is added.
///
/// <para>
/// This test introspects the planned schema objects via <see cref="StoreOptions.Validate"/>
/// without touching the database — kept in its own class (no fixture) because
/// it doesn't actually need the shared store's tenant partitions, just an
/// in-memory <see cref="StoreOptions"/> instance.
/// </para>
/// </summary>
public class event_progression_schema_layout
{
    [Fact]
    public void event_progression_table_pk_stays_name_only_with_per_tenant_flag_on()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = "tp_progression_schema_layout";
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Validate();

        var schemaObjects = ((Weasel.Core.Migrations.IFeatureSchema)opts.EventGraph).Objects;
        var progression = schemaObjects.OfType<Table>()
            .Single(t => t.Identifier.Name == EventProgressionTable.Name);

        progression.PrimaryKeyColumns.ShouldBe(new[] { "name" },
            "Session 3 keeps the existing single-column PK — per-tenant separation lives inside ShardName.Identity.");
        progression.Columns.Any(c => c.Name == "tenant_id")
            .ShouldBeFalse("No tenant_id column on mt_event_progression — per-tenant rows share the table via distinct name values.");
    }
}
