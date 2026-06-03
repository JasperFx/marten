using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using Marten;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Migrated from MultiTenancyTests/use_tenant_partitioned_events_vectorized_high_water.cs
/// — the two flag-OFF fallback assertions: with
/// <see cref="Marten.Events.EventGraph.UseTenantPartitionedEvents"/> = false
/// the detector must collapse to the single store-global reading and
/// <see cref="ICrossTenantRebuildSource.FindRebuildTenantsAsync"/> must
/// return empty. These two tests build their own non-partitioned
/// <see cref="DocumentStore"/> per test because they're specifically
/// asserting the absence of the per-tenant machinery — the shared
/// fixture's store has the flag ON, so it can't host these assertions.
/// </summary>
public class vectorized_high_water_flag_off
{
    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    public record Probe(string Label);

    [Fact]
    public async Task detect_for_tenants_async_falls_back_to_global_when_per_tenant_flag_is_off()
    {
        var schema = $"tp_hw_off_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 40);
        await ResetSchemaAsync(schema);

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            // UseTenantPartitionedEvents stays false — single-mark store
            o.Events.AddEventType<Probe>();
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var detector = new HighWaterDetector(
            (MartenDatabase)store.Storage.Database, store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new[] { "alpha", "beta" }, CancellationToken.None);

        vector.Global.ShouldNotBeNull(
            "with the per-tenant flag off, the detector must collapse to the single store-global reading regardless of supplied tenant ids");
        vector.TenantCount.ShouldBe(0,
            "no per-tenant readings in the non-partitioned fallback");
    }

    [Fact]
    public async Task find_rebuild_tenants_async_returns_empty_when_tenant_partitions_not_configured()
    {
        var schema = $"tp_xt_off_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 40);
        await ResetSchemaAsync(schema);

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            // Per-tenant flag off → TenantPartitions stays null
            o.Events.AddEventType<Probe>();
        });
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var source = (ICrossTenantRebuildSource)store.Storage.Database;
        var tenants = await source.FindRebuildTenantsAsync("AnyProjection", CancellationToken.None);

        tenants.ShouldBeEmpty();
    }
}
