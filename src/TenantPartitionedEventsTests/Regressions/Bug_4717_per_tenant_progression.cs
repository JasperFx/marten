#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #4717 — under per-tenant event partitioning the async daemon must persist PER-TENANT progression
/// records, not a single store-global <c>&lt;Projection&gt;:All</c> row. Each tenant's events come from
/// its own <c>mt_events_sequence_{suffix}</c> starting at 1, so with two+ tenants their seq_id ranges
/// overlap and a single <c>:All</c> shard cannot represent "how far each tenant has been projected".
///
/// <para>
/// This proves the requirement directly: two tenants with DIFFERENT event counts on one
/// per-tenant-partitioned database, running BOTH a composite projection and a standalone async
/// projection continuously, must produce a per-tenant progression row per (projection, tenant) whose
/// last_seq_id is that tenant's own height — plus a per-tenant high-water row per tenant.
/// </para>
/// </summary>
public partial class Bug_4717_per_tenant_progression
{
    private readonly ITestOutputHelper _output;

    public Bug_4717_per_tenant_progression(ITestOutputHelper output) => _output = output;

    public class Bug4717Trip { public Guid Id { get; set; } public double Distance { get; set; } }
    public class Bug4717Count { public Guid Id { get; set; } public int Count { get; set; } }
    public class Bug4717Standalone { public Guid Id { get; set; } public double Total { get; set; } }

    public record Bug4717Started(Guid Id);
    public record Bug4717Leg(double Distance);

    public partial class Bug4717TripProjection: SingleStreamProjection<Bug4717Trip, Guid>
    {
        public Bug4717TripProjection() => Name = "Bug4717Trip";
        public void Apply(Bug4717Trip a, Bug4717Leg e) => a.Distance += e.Distance;
    }

    public partial class Bug4717CountProjection: SingleStreamProjection<Bug4717Count, Guid>
    {
        public Bug4717CountProjection() => Name = "Bug4717Count";
        public void Apply(Bug4717Count a, Bug4717Leg e) => a.Count++;
    }

    public partial class Bug4717StandaloneProjection: SingleStreamProjection<Bug4717Standalone, Guid>
    {
        public Bug4717StandaloneProjection() => Name = "Bug4717Standalone";
        public void Apply(Bug4717Standalone a, Bug4717Leg e) => a.Total += e.Distance;
    }

    private static readonly string Schema = $"bug4717_p{Environment.ProcessId}";

    [Fact]
    public async Task daemon_persists_per_tenant_progression_for_standalone_and_composite()
    {
        using var store = (DocumentStore)DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = Schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Schema.For<Bug4717Trip>().DocumentAlias("b4717_trip");
            o.Schema.For<Bug4717Count>().DocumentAlias("b4717_cnt");
            o.Schema.For<Bug4717Standalone>().DocumentAlias("b4717_std");

            // A standalone async projection AND a composite bundle, both continuous.
            o.Projections.Add<Bug4717StandaloneProjection>(ProjectionLifecycle.Async);
            o.Projections.CompositeProjectionFor("bug4717-composite", c =>
            {
                c.Add<Bug4717TripProjection>(stageNumber: 1);
                c.Add<Bug4717CountProjection>(stageNumber: 2);
            });
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Two tenants with DIFFERENT event heights — independent per-tenant sequences.
        var streamsPerTenant = new Dictionary<string, int> { ["t4717_a"] = 5, ["t4717_b"] = 3 };
        var expectedSeq = streamsPerTenant.ToDictionary(p => p.Key, p => (long)(p.Value * 4)); // 4 events/stream
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, streamsPerTenant.Keys.ToArray());

        foreach (var (tenant, streams) in streamsPerTenant)
        {
            await using var session = store.LightweightSession(tenant);
            for (var s = 0; s < streams; s++)
            {
                var id = Guid.NewGuid();
                session.Events.StartStream<Bug4717Trip>(id,
                    new Bug4717Started(id), new Bug4717Leg(1.0), new Bug4717Leg(2.0), new Bug4717Leg(3.0));
            }

            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // Poll until every (projection, tenant) has a per-tenant progression row at the expected height.
        var projections = new[] { "Bug4717Standalone", "Bug4717Trip", "Bug4717Count", "bug4717-composite" };
        var sw = Stopwatch.StartNew();
        List<(string Name, long Seq)> rows = new();
        while (sw.Elapsed < 30.Seconds())
        {
            rows = await progressionRowsAsync();
            var allReady = projections.All(p => streamsPerTenant.Keys.All(t =>
                rows.Any(r => isPerTenantRow(r.Name, p, t) && r.Seq >= expectedSeq[t])));
            if (allReady) break;
            await Task.Delay(500);
        }

        await daemon.StopAllAsync();

        _output.WriteLine("=== mt_event_progression ===");
        foreach (var (name, seq) in rows.OrderBy(r => r.Name)) _output.WriteLine($"{seq,6} | {name}");

        // Per-tenant PROJECTION progress: each (projection, tenant) tracked independently at the
        // tenant's own height. Today only store-global "<Projection>:All" rows are written (#4717).
        foreach (var projection in new[] { "Bug4717Standalone", "Bug4717Trip", "Bug4717Count" })
        {
            foreach (var (tenant, seq) in expectedSeq)
            {
                var row = rows.FirstOrDefault(r => isPerTenantRow(r.Name, projection, tenant));
                row.Name.ShouldNotBeNull(
                    $"expected a per-tenant progression row for {projection} / tenant {tenant} " +
                    $"(rows: {string.Join("; ", rows.Select(r => $"{r.Name}={r.Seq}"))})");
                row.Seq.ShouldBe(seq,
                    $"{projection} for tenant {tenant} should track its own sequence height {seq}");
            }
        }

        // Per-tenant HIGH-WATER: one row per tenant, at the tenant's own height.
        foreach (var (tenant, seq) in expectedSeq)
        {
            var hw = rows.FirstOrDefault(r => r.Name == $"HighWaterMark:{tenant}");
            hw.Name.ShouldNotBeNull($"expected a per-tenant HighWaterMark row for tenant {tenant}");
            hw.Seq.ShouldBe(seq, $"HighWaterMark for tenant {tenant} should be its own height {seq}");
        }
    }

    // A per-tenant row carries the tenant in the trailing slot of the ShardName grammar
    // ("<Projection>:<shardKey>:<tenant>"), e.g. "Bug4717Standalone:All:t4717_a".
    private static bool isPerTenantRow(string name, string projection, string tenant) =>
        name.StartsWith(projection + ":", StringComparison.Ordinal) &&
        name.EndsWith(":" + tenant, StringComparison.Ordinal);

    private static async Task<List<(string Name, long Seq)>> progressionRowsAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select name, coalesce(last_seq_id,0) from {Schema}.mt_event_progression order by name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, long)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetInt64(1)));
        return rows;
    }
}
