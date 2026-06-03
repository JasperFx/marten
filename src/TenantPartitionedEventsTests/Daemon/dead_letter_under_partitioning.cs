using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #4617 section 4 deferred — pin <see cref="DeadLetterEvent"/> mapping and
/// partition-policy carve-out under <c>UseTenantPartitionedEvents</c>.
///
/// <list type="bullet">
///   <item>
///     <see cref="DeadLetterEvent"/> stays <see cref="TenancyStyle.Single"/>
///     even with <c>AllDocumentsAreMultiTenanted</c> active. The store-wide
///     dead-letter table is shared by every tenant by design — daemon
///     diagnostics aren't a tenant boundary.
///   </item>
///   <item>
///     <c>MartenManagedTenantListPartitions.applyPartitioning</c> explicitly
///     skips the <see cref="DeadLetterEvent"/> mapping so the table doesn't
///     pick up a tenant-id LIST partition declaration that would otherwise
///     follow from the multi-tenanted policy. Pinned by inspecting the
///     materialized mapping — no partitioning expression on its primary key.
///   </item>
/// </list>
///
/// <para>
/// Own-store because this pin needs a specific store configuration shape
/// (partitioning + multi-tenant policy + opt-in to including DeadLetterEvent
/// in the schema) to be meaningful.
/// </para>
/// </summary>
public class dead_letter_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_dlq_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            // Touch the events feature so MartenManagedTenantListPartitions
            // runs its applyPartitioning sweep over every doc mapping at
            // schema-feature build time.
            opts.Events.AddEventType<DlqProbeEvent>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void dead_letter_event_mapping_stays_single_tenanted_under_partitioning()
    {
        // Even with UseTenantPartitionedEvents + AllDocumentsAreMultiTenanted,
        // the DeadLetterEvent table stays store-global. Pin so a future change
        // that auto-multi-tenants every mapping (or removes the explicit
        // carve-out in MartenManagedTenantListPartitions / StoreOptions) is a
        // deliberate contract change.
        _store.StorageFeatures.MappingFor(typeof(DeadLetterEvent)).TenancyStyle
            .ShouldBe(TenancyStyle.Single,
                "DeadLetterEvent is store-global by design — daemon diagnostics aren't a tenant boundary");
    }

    [Fact]
    public void dead_letter_event_lives_under_events_schema_not_doc_schema()
    {
        // StoreOptions.cs:743 explicitly routes DeadLetterEvent's table to
        // Events.DatabaseSchemaName (not the default doc schema). Pin the
        // schema placement so a future refactor that moves it doesn't silently
        // strand existing dead-letter rows.
        var mapping = _store.StorageFeatures.MappingFor(typeof(DeadLetterEvent));
        mapping.DatabaseSchemaName.ShouldBe(_store.Options.Events.DatabaseSchemaName);
    }

    [Fact]
    public void dead_letter_event_table_has_no_tenant_id_partition_declaration_under_partitioning()
    {
        // MartenManagedTenantListPartitions.applyPartitioning has an explicit
        // `if (mapping.DocumentType == typeof(DeadLetterEvent)) return;` carve-
        // out — without it, the multi-tenanted policy would push the dead-
        // letter table into a tenant-id LIST partition declaration, which
        // would (a) require AddMartenManagedTenantsAsync to onboard every
        // dead-letter consumer, and (b) silo dead-letter rows per tenant when
        // the contract is store-global. Pin by querying the live partition
        // map for the dead-letter table — it must NOT be registered.
        var dlqTable = _store.Options.TenantPartitions?.Partitions;
        if (dlqTable == null) return; // no partition manager active — nothing to pin

        // Pull the underlying partitioned-table set; the dead-letter table
        // must not appear there.
        var allPartitionedTableNames = _store.Storage.AllObjects()
            .OfType<Weasel.Postgresql.Tables.Table>()
            .Where(t => t.Partitioning is Weasel.Postgresql.Tables.Partitioning.ListPartitioning)
            .Select(t => t.Identifier.Name)
            .ToList();

        allPartitionedTableNames.ShouldNotContain(name => name.Contains("mt_doc_deadletterevent"),
            "DeadLetterEvent must NOT be partitioned by tenant — it's store-global by design");
    }
}

public record DlqProbeEvent(string Label);
