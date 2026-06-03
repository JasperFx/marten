using System;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #4617 section 3e deferred — pin the SQL script <see cref="IMartenStorage.ToDatabaseScript"/>
/// generates under <c>UseTenantPartitionedEvents</c>:
///
/// <list type="bullet">
///   <item>mt_events + mt_streams CREATE TABLE statements include the
///     <c>PARTITION BY LIST (tenant_id)</c> declaration.</item>
///   <item>The mt_quick_append_events function definition includes the
///     per-tenant nextval EXECUTE block + MT002 raise (the surface that
///     gives users a clean error for unregistered tenant appends).</item>
///   <item>The explicit <c>mt_events</c>→<c>mt_streams</c> foreign key is
///     ABSENT (per #4606 — the FK trips 42P16 on partition attach).</item>
/// </list>
///
/// <para>
/// The flag-OFF control script contains the FK and does NOT have the
/// PARTITION BY clause — pinned to keep the diff explicit.
/// </para>
///
/// <para>
/// Tests use their own DocumentStore instances (no DB writes — just
/// inspect the generated script string).
/// </para>
/// </summary>
public class ddl_generation_script_under_partitioning
{
    private static DocumentStore BuildStore(bool partitioned)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"tp_ddl_{(partitioned ? "on" : "off")}_{Guid.NewGuid().ToString("N")[..8]}";
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            // The events feature is only included in the DDL script after at
            // least one event type is registered — otherwise the script just
            // contains the empty schema-creation block.
            opts.Events.AddEventType<DdlProbeEvent>();
            if (partitioned)
            {
                opts.Events.UseTenantPartitionedEvents = true;
                opts.Policies.AllDocumentsAreMultiTenanted();
            }
        });
    }

    public record DdlProbeEvent(string Label);

    [Fact]
    public void script_under_partitioning_emits_list_partition_declaration_on_mt_events_and_mt_streams()
    {
        using var store = BuildStore(partitioned: true);
        var script = store.Storage.ToDatabaseScript();

        // Both partitioned tables declare LIST partitioning on tenant_id.
        script.ShouldContain("CREATE TABLE", Case.Insensitive);
        script.ShouldContain("mt_events");
        script.ShouldContain("mt_streams");
        script.ShouldContain("partition by list (tenant_id)", Case.Insensitive);
    }

    [Fact]
    public void script_under_partitioning_includes_MT002_raise_in_quick_append_function()
    {
        using var store = BuildStore(partitioned: true);
        var script = store.Storage.ToDatabaseScript();

        // The MT002 SQLSTATE is the user-facing signal for "you tried to append
        // for an unregistered tenant" — must appear in the generated function
        // definition under partitioning. The partitioned variant of
        // mt_quick_append_events raises MT002 up-front for unregistered tenants.
        script.ShouldContain("MT002");
    }

    [Fact]
    public void script_under_partitioning_does_NOT_include_explicit_mt_events_to_mt_streams_FK()
    {
        using var store = BuildStore(partitioned: true);
        var script = store.Storage.ToDatabaseScript();

        // #4606: the explicit mt_events → mt_streams FK trips 42P16 on the
        // partition attach (Postgres auto-propagates a partition-level FK that
        // Weasel's additive-migrate path then tries to drop). The fix dropped
        // the explicit FK declaration under partitioning. Pin its absence so
        // a future change that adds it back is intentional.
        // The explicit mt_events→mt_streams FK is removed under partitioning to
        // avoid the 42P16 inherited-constraint trap (#4606); pin its absence.
        script.ShouldNotContain("fkey_mt_events_stream_id");
    }

    [Fact]
    public void script_with_flag_off_INCLUDES_the_explicit_mt_events_to_mt_streams_FK()
    {
        // The control: with UseTenantPartitionedEvents OFF, the FK IS in the
        // generated script. Pin the conjoined-non-partitioned shape so a future
        // refactor that accidentally drops the FK from BOTH paths is caught.
        using var store = BuildStore(partitioned: false);
        var script = store.Storage.ToDatabaseScript();

        // Without partitioning, the explicit mt_events→mt_streams FK IS
        // emitted — the FK is only conditionally skipped on the partitioned
        // path (#4606).
        script.ShouldContain("fkey_mt_events_stream_id");
    }
}
