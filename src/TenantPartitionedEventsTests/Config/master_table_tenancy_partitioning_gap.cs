using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Config;

/// <summary>
/// #4617 section 3d — THE GAP: combining <c>UseTenantPartitionedEvents</c>
/// with <c>MultiTenantedDatabasesWithMasterDatabaseTable</c>
/// (<see cref="MasterTableTenancy"/>) is unsupported, but
/// <c>StoreOptions.Validate()</c> does NOT reject the combo at configuration
/// time. The user-visible failure shows up later when they call
/// <c>AddMartenManagedTenantsAsync</c> — and then again at first event append
/// because nothing provisions the per-tenant partition / sequence in each
/// per-tenant database. These tests pin the actual current behavior so a
/// future fail-fast guard in <c>Validate()</c> gets intentional review rather
/// than accidental drift.
/// </summary>
public class master_table_tenancy_partitioning_gap
{
    [Fact]
    public void validate_does_NOT_reject_MasterTableTenancy_combined_with_partitioning()
    {
        // The gap: Validate's per-tenant-partitioning guards cover Rich append,
        // non-Conjoined tenancy, and ArchivedStreamPartitioning — but NOT
        // MasterTableTenancy. Build a fully-configured options object and call
        // Validate(); assert it doesn't throw. If this assertion ever starts
        // failing, the guard was finally added — update the test to assert the
        // new throw shape instead.
        var opts = new StoreOptions();
        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
        {
            x.ConnectionString = ConnectionSource.ConnectionString;
            x.SchemaName = "tp_gap_master_" + Guid.NewGuid().ToString("N")[..8];
        });
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

        Should.NotThrow(() => opts.Validate(),
            "Validate() currently does NOT reject MasterTableTenancy + UseTenantPartitionedEvents — " +
            "the user-visible failure surfaces later. If this starts throwing, a fail-fast guard has " +
            "been added; update the test to pin the new shape.");
    }

    [Fact]
    public async Task AddMartenManagedTenantsAsync_throws_for_MasterTableTenancy()
    {
        // Once the user gets past Validate(), the next step they typically take
        // is AddMartenManagedTenantsAsync — and that's where the actual rejection
        // happens. Pin the exception shape (InvalidOperationException + message
        // mentioning the supported tenancy modes) so users get a clear signal.
        using var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "tp_gap_master_" + Guid.NewGuid().ToString("N")[..8];
            });
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        });

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "tenant1"));

        ex.Message.ShouldContain("DefaultTenancy");
        ex.Message.ShouldContain("ShardedTenancy");
    }
}
