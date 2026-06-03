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
/// #4617 section 3d — same gap as <see cref="master_table_tenancy_partitioning_gap"/>
/// but for <c>MultiTenantedWithSingleServer</c>
/// (<see cref="SingleServerMultiTenancy"/>). <c>Validate()</c> doesn't reject
/// the combo and <c>AddMartenManagedTenantsAsync</c> rejects later. Pinning
/// both shapes so future hardening (a config-time guard) is a deliberate
/// change rather than accidental drift.
/// </summary>
public class single_server_multi_tenancy_partitioning_gap
{
    [Fact]
    public void validate_does_NOT_reject_SingleServerMultiTenancy_combined_with_partitioning()
    {
        var opts = new StoreOptions();
        opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, tenancy =>
        {
            tenancy.WithTenants("t1", "t2").InDatabaseNamed("tp_gap_ss_" + Guid.NewGuid().ToString("N")[..8]);
        });
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

        Should.NotThrow(() => opts.Validate(),
            "Validate() currently does NOT reject SingleServerMultiTenancy + UseTenantPartitionedEvents — " +
            "the user-visible failure surfaces later. If this starts throwing, a fail-fast guard has been " +
            "added; update the test to pin the new shape.");
    }

    [Fact]
    public async Task AddMartenManagedTenantsAsync_throws_for_SingleServerMultiTenancy()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, tenancy =>
            {
                tenancy.WithTenants("t1", "t2").InDatabaseNamed("tp_gap_ss_" + Guid.NewGuid().ToString("N")[..8]);
            });
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        });

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "t1"));

        ex.Message.ShouldContain("DefaultTenancy");
        ex.Message.ShouldContain("ShardedTenancy");
    }
}
