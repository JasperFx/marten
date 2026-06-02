using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4596 Phase 2 — vectorized per-tenant high-water + cross-tenant rebuild source.
///
/// <para>
/// JasperFx 2.5.0-pt209.3 shipped the store-agnostic Phase 2 daemon
/// abstractions (<see cref="IHighWaterDetector.DetectForTenantsAsync"/> /
/// <see cref="IHighWaterDetector.DetectInSafeZoneForTenantsAsync"/>,
/// <see cref="HighWaterVector"/>, <see cref="VectorizedHighWaterMonitor"/>,
/// <see cref="PolledTenantSet"/>, <see cref="ICrossTenantRebuildSource"/>,
/// <see cref="CrossTenantRebuild"/>). This file pins the Marten-side
/// implementations:
/// </para>
///
/// <list type="bullet">
///   <item><description>Marten's <see cref="HighWaterDetector"/> overrides
///     <c>DetectForTenantsAsync</c> / <c>DetectInSafeZoneForTenantsAsync</c> to
///     query each polled tenant's <c>mt_events_sequence_{suffix}</c> +
///     per-tenant high-water row in a single round-trip. With the flag off,
///     the default falls through to the existing store-global Detect.</description></item>
///   <item><description><see cref="MartenDatabase"/> implements
///     <c>ICrossTenantRebuildSource.FindRebuildTenantsAsync</c> by enumerating
///     registered tenants from <c>mt_tenant_partitions</c>.</description></item>
///   <item><description><see cref="VectorizedHighWaterMonitor"/> (jasperfx)
///     consumes the Marten detector through the published interface — its
///     independent per-tenant gap detection is verified at the orchestration
///     level in jasperfx's own tests, but the round-tripping against a real
///     concrete store is pinned here.</description></item>
/// </list>
/// </summary>
public class use_tenant_partitioned_events_vectorized_high_water
{
    private const string Schema = "tenant_partitioned_events_session_p2";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static DocumentStore BuildStore(string schema)
    {
        return DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Events.AddEventType<Probe>();
        });
    }

    public record Probe(string Label);

    // ---- Vectorized high-water detector ----

    [Fact]
    public async Task detect_for_tenants_async_returns_one_reading_per_polled_tenant_in_one_roundtrip()
    {
        var schema = Schema + "_vec";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta", "gamma");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Heterogeneous tenant states: alpha gets 5 events, beta gets 2, gamma
        // gets none (its per-tenant sequence stays at 0 last_value).
        await AppendAsync(store, "alpha", 5);
        await AppendAsync(store, "beta", 2);

        var detector = new HighWaterDetector(
            (MartenDatabase)store.Storage.Database, store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new[] { "alpha", "beta", "gamma" }, CancellationToken.None);

        vector.TenantCount.ShouldBe(3,
            "vectorized detector must emit one reading per polled tenant, even when the tenant has no events yet");

        vector.TryGetStatistics("alpha", out var alphaStat).ShouldBeTrue();
        vector.TryGetStatistics("beta", out var betaStat).ShouldBeTrue();
        vector.TryGetStatistics("gamma", out var gammaStat).ShouldBeTrue();

        alphaStat.TenantId.ShouldBe("alpha");
        betaStat.TenantId.ShouldBe("beta");
        gammaStat.TenantId.ShouldBe("gamma");

        // Per-tenant `HighestSequence` reflects each tenant's own
        // `mt_events_sequence_{suffix}.last_value` — that's the headline
        // independence signal. Gamma's reading is intact even though it has
        // no events (gap detection isn't stalled by gamma being flat).
        alphaStat.HighestSequence.ShouldBe(5L);
        betaStat.HighestSequence.ShouldBe(2L);
        gammaStat.HighestSequence.ShouldBe(0L);
    }

    [Fact]
    public async Task detect_for_tenants_async_returns_empty_vector_when_no_tenants_polled()
    {
        var schema = Schema + "_empty";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var detector = new HighWaterDetector(
            (MartenDatabase)store.Storage.Database, store.Options.EventGraph, NullLogger.Instance);

        var vector = await ((IHighWaterDetector)detector).DetectForTenantsAsync(
            new string[0], CancellationToken.None);

        vector.TenantCount.ShouldBe(0);
        vector.Global.ShouldBeNull();
    }

    [Fact]
    public async Task detect_for_tenants_async_falls_back_to_global_when_per_tenant_flag_is_off()
    {
        var schema = Schema + "_off";
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

    // ---- VectorizedHighWaterMonitor integration with the Marten detector ----

    [Fact]
    public async Task monitor_polls_only_assigned_tenants_and_advances_ceiling_per_tenant()
    {
        var schema = Schema + "_monitor";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta", "gamma");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await AppendAsync(store, "alpha", 3);
        await AppendAsync(store, "beta", 1);

        var detector = new HighWaterDetector(
            (MartenDatabase)store.Storage.Database, store.Options.EventGraph, NullLogger.Instance);
        var monitor = new VectorizedHighWaterMonitor(detector);

        // PolledTenantSet starts empty — the daemon activates a tenant when one
        // of its shards lands on this node, deactivates when its last shard
        // leaves. The monitor only ever polls the currently-activated set.
        monitor.PolledTenants.Activate("alpha").ShouldBeTrue();
        monitor.PolledTenants.Activate("beta").ShouldBeTrue();
        // gamma intentionally NOT activated; should not appear in the poll.

        var readings = await monitor.PollAsync(CancellationToken.None);

        readings.Select(r => r.TenantId).OrderBy(t => t).ShouldBe(new[] { "alpha", "beta" });
        monitor.CeilingFor("alpha").ShouldBe(3L,
            "per-tenant rebuild ceiling = the alpha sequence's last_value");
        monitor.CeilingFor("beta").ShouldBe(1L);
        monitor.CeilingFor("gamma").ShouldBeNull(
            "gamma was never polled because it wasn't activated on this node — the monitor doesn't see it");
    }

    [Fact]
    public async Task polled_set_deactivate_removes_tenant_from_subsequent_polls()
    {
        var schema = Schema + "_deact";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        await AppendAsync(store, "alpha", 1);
        await AppendAsync(store, "beta", 1);

        var detector = new HighWaterDetector(
            (MartenDatabase)store.Storage.Database, store.Options.EventGraph, NullLogger.Instance);
        var monitor = new VectorizedHighWaterMonitor(detector);

        monitor.PolledTenants.Activate("alpha");
        monitor.PolledTenants.Activate("beta");

        (await monitor.PollAsync(CancellationToken.None)).Count.ShouldBe(2);

        // Deactivate alpha (simulates its last shard being redistributed off
        // this node by Wolverine).
        monitor.PolledTenants.Deactivate("alpha").ShouldBeTrue();

        var next = await monitor.PollAsync(CancellationToken.None);
        next.Select(r => r.TenantId).ShouldBe(new[] { "beta" });
    }

    // ---- ICrossTenantRebuildSource ----

    [Fact]
    public async Task find_rebuild_tenants_async_returns_every_registered_tenant_partition()
    {
        var schema = Schema + "_xtenant";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(
            CancellationToken.None, "tenant_a", "tenant_b", "tenant_c");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var source = (ICrossTenantRebuildSource)store.Storage.Database;
        var tenants = await source.FindRebuildTenantsAsync("AnyProjection", CancellationToken.None);

        tenants.OrderBy(t => t).ToList().ShouldBe(new[] { "tenant_a", "tenant_b", "tenant_c" });
    }

    [Fact]
    public async Task find_rebuild_tenants_async_returns_empty_when_tenant_partitions_not_configured()
    {
        var schema = Schema + "_xtenant_off";
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

    // ---- helpers ----

    private static async Task AppendAsync(DocumentStore store, string tenantId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await using var session = store.LightweightSession(tenantId);
            session.Events.StartStream(Guid.NewGuid(), new Probe($"{tenantId}-{i}"));
            await session.SaveChangesAsync();
        }
    }
}
