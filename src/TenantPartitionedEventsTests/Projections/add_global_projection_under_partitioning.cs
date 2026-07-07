using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c deferred — pin <c>AddGlobalProjection</c> behavior under
/// <c>UseTenantPartitionedEvents</c>: the projected aggregate doc keeps
/// <c>TenancyStyle.Single</c> while source events stay partitioned per tenant.
/// Sessions can read the global doc without scoping by tenant.
///
/// <para>
/// Own-store because <c>AddGlobalProjection</c> is store-config-level: it
/// registers a projection whose aggregate doc is explicitly opted-out of
/// the <c>AllDocumentsAreMultiTenanted</c> policy.
/// </para>
/// </summary>
public class add_global_projection_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_glob_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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

            opts.Events.AddEventType<GlobalTickEvent>();

            // The headline: register a SingleStreamProjection as GLOBAL — its
            // aggregate doc keeps TenancyStyle.Single even though the source
            // event store is partitioned per tenant and the default policy
            // multi-tenants every other doc.
            opts.Projections.AddGlobalProjection(new GlobalCounterProjection(), ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void global_projection_doc_is_TenancyStyle_Single_under_partitioning()
    {
        // Even with UseTenantPartitionedEvents on + AllDocumentsAreMultiTenanted,
        // the global projection's aggregate doc stays Single-tenanted by design.
        // Pin so a future change that auto-multi-tenants the global doc is a
        // deliberate contract change.
        _store.StorageFeatures.MappingFor(typeof(GlobalCounter)).TenancyStyle
            .ShouldBe(TenancyStyle.Single);
    }

    [Fact]
    public async Task append_to_global_aggregate_succeeds_and_rolls_up_across_tenants()
    {
        // #4648 fix (flips the former MT002 pin): AddGlobalProjection's
        // GlobalEventAppenderDecorator routes the global aggregate's events to the
        // *DEFAULT* tenant slot. That sentinel can't be a partition-table SUFFIX
        // (illegal identifier characters), but a LIST partition VALUE can be any
        // string — so AddMartenManagedTenantsAsync now auto-provisions a reserved
        // '__default__' suffix for the '*DEFAULT*' partition value whenever the
        // store has global aggregates registered. The rerouted appends then land
        // in a real partition (with their own per-tenant event sequence) instead
        // of raising MT002.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var globalId = Guid.NewGuid();

        // Two different tenants funnel events into the SAME global stream
        await using (var alpha = _store.LightweightSession("alpha"))
        {
            alpha.Events.StartStream<GlobalCounter>(globalId,
                new GlobalTickEvent("first"), new GlobalTickEvent("second"));
            await alpha.SaveChangesAsync();
        }

        await using (var beta = _store.LightweightSession("beta"))
        {
            beta.Events.Append(globalId, new GlobalTickEvent("third"));
            await beta.SaveChangesAsync();
        }

        // The inline global projection doc is single-tenanted, so it reads the
        // same from any tenant's session — and reflects BOTH tenants' appends
        await using (var reader = _store.QuerySession("beta"))
        {
            var counter = await reader.LoadAsync<GlobalCounter>(globalId);
            counter.ShouldNotBeNull();
            counter.TickCount.ShouldBe(3);
        }

        // The storage-level shape: the default tenant slot is registered with the
        // reserved suffix and all three events live in its partition
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var suffix = (string?)await conn.CreateCommand(
                $"select partition_suffix from {_schema}.mt_tenant_partitions where partition_value = '{StorageConstants.DefaultTenantId}'")
            .ExecuteScalarAsync();
        suffix.ShouldBe("__default__");

        var eventCount = (long)(await conn.CreateCommand(
                $"select count(*) from {_schema}.mt_events___default__")
            .ExecuteScalarAsync())!;
        eventCount.ShouldBe(3);
    }

    [Fact]
    public async Task reserved_default_suffix_is_rejected_for_regular_tenants()
    {
        // A regular tenant may not claim the '__default__' suffix reserved for the
        // global-projection default tenant slot — a shared suffix would fold two
        // partition VALUES into one partition table and corrupt tenant isolation.
        var ex = await Should.ThrowAsync<ArgumentException>(() =>
            _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,
                new System.Collections.Generic.Dictionary<string, string> { ["acme"] = "__default__" }));

        ex.Message.ShouldContain("reserved");
    }
}

public record GlobalTickEvent(string Label);

public class GlobalCounter
{
    public Guid Id { get; set; }
    public int TickCount { get; set; }
}

public partial class GlobalCounterProjection : SingleStreamProjection<GlobalCounter, Guid>
{
    public GlobalCounterProjection() { Name = "GlobalCounter"; }
    public void Apply(GlobalCounter c, GlobalTickEvent _) => c.TickCount++;
}
