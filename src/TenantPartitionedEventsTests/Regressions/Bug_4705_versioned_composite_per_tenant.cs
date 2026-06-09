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
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #4705 — composite projection shards stalled at last_seq_id=1 under per-tenant event partitioning
/// while a standalone async projection advanced to the high-water.
///
/// <para>
/// Root cause: a composite runs an "optimized rebuild" via <c>CompositeReplayExecutor</c> whose ceiling
/// comes from <c>IEventDatabase.FetchHighestEventSequenceNumber()</c>. Marten implemented that as
/// <c>select last_value from mt_events_sequence</c> — the store-global sequence, never advanced under
/// <c>UseTenantPartitionedEvents</c> (per-tenant <c>mt_events_sequence_{suffix}</c> carry the real
/// seq_ids), so it read as 1 and the composite replayed only events 0..1. Fixed by making
/// <c>FetchHighestEventSequenceNumber</c> read <c>max(seq_id)</c> from <c>mt_events</c> under per-tenant
/// partitioning. The standalone projection was always immune — its continuous agent is driven by the
/// high-water detector (<c>HighWaterMark</c>), not that method.
/// </para>
///
/// <para>
/// This guard runs the scenario at version 1 AND version 2 to show the stall was never about the
/// projection version (the reporter's stalled shards merely happened to be versioned); both must reach
/// the high-water. Single-DB, single tenant — per-tenant partitioning is the only load-bearing factor.
/// </para>
/// </summary>
public partial class Bug_4705_versioned_composite_per_tenant
{
    private readonly ITestOutputHelper _output;

    // Read by the projection constructors at registration time. The theory sets it before building
    // the store; xUnit runs theory cases sequentially within a class, and each case uses its own
    // schema, so there is no cross-case bleed.
    private static uint _version = 1;

    public Bug_4705_versioned_composite_per_tenant(ITestOutputHelper output) => _output = output;

    public class Bug4705Trip { public Guid Id { get; set; } public double Distance { get; set; } public int Version { get; set; } }
    public class Bug4705Count { public Guid Id { get; set; } public int Count { get; set; } public int Version { get; set; } }
    public class Bug4705Standalone { public Guid Id { get; set; } public double Total { get; set; } public int Version { get; set; } }

    public record Bug4705Started(Guid Id);
    public record Bug4705Leg(double Distance);

    public partial class Bug4705TripProjection: SingleStreamProjection<Bug4705Trip, Guid>
    {
        public Bug4705TripProjection() { Name = "Bug4705Trip"; Version = _version; }
        public void Apply(Bug4705Trip a, Bug4705Leg e) => a.Distance += e.Distance;
    }

    public partial class Bug4705CountProjection: SingleStreamProjection<Bug4705Count, Guid>
    {
        public Bug4705CountProjection() { Name = "Bug4705Count"; Version = _version; }
        public void Apply(Bug4705Count a, Bug4705Leg e) => a.Count++;
    }

    // Control: a standalone async projection (matches the reporter's InvoiceJournalEntries that DOES advance).
    public partial class Bug4705StandaloneProjection: SingleStreamProjection<Bug4705Standalone, Guid>
    {
        public Bug4705StandaloneProjection() { Name = "Bug4705Standalone"; Version = _version; }
        public void Apply(Bug4705Standalone a, Bug4705Leg e) => a.Total += e.Distance;
    }

    private static string SchemaFor(uint version) => $"bug4705_v{version}_p{Environment.ProcessId}";

    private static void configure(StoreOptions opts, uint version)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = SchemaFor(version);
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Policies.AllDocumentsAreMultiTenanted();

        // Short doc aliases — the nested test class names overflow PostgreSQL's 64-char identifier limit.
        opts.Schema.For<Bug4705Trip>().DocumentAlias("b4705_trip");
        opts.Schema.For<Bug4705Count>().DocumentAlias("b4705_cnt");
        opts.Schema.For<Bug4705Standalone>().DocumentAlias("b4705_std");

        // Standalone control.
        opts.Projections.Add<Bug4705StandaloneProjection>(ProjectionLifecycle.Async);

        // Versioned composite bundle with two SingleStream members.
        opts.Projections.CompositeProjectionFor("bug4705-composite", c =>
        {
            c.Version = version;
            c.Add<Bug4705TripProjection>(stageNumber: 1);
            c.Add<Bug4705CountProjection>(stageNumber: 2);
        });
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public async Task continuous_composite_under_per_tenant_partitioning(uint version)
    {
        _version = version;
        var schema = SchemaFor(version);

        using var store = (DocumentStore)DocumentStore.For(o => configure(o, version));
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // #4705 reporter seeds a SINGLE tenant. That matters under per-tenant partitioning: each
        // tenant's events use a per-tenant sequence starting at 1, so seq_id is only globally unique
        // when there is one tenant. With one tenant a store-global :All shard can page the global
        // seq cursor correctly (the standalone advances); the composite stall is then isolated.
        var tenants = new[] { "t4705_solo" };
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        long appended = 0;
        foreach (var tenant in tenants)
        {
            await using var session = store.LightweightSession(tenant);
            for (var s = 0; s < 10; s++)
            {
                var streamId = Guid.NewGuid();
                session.Events.StartStream<Bug4705Trip>(streamId,
                    new Bug4705Started(streamId), new Bug4705Leg(1.0), new Bug4705Leg(2.0), new Bug4705Leg(3.0));
                appended += 4;
            }

            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // Poll the progression rows until the composite catches up to the high-water, or time out.
        var sw = Stopwatch.StartNew();
        List<(string Name, long Seq)> rows = new();
        long highWater = 0, composite = 0, standalone = 0;
        while (sw.Elapsed < 25.Seconds())
        {
            rows = await progressionRowsAsync(schema);
            highWater = rows.FirstOrDefault(r => r.Name == "HighWaterMark").Seq;
            composite = rows.Where(r => r.Name.StartsWith("bug4705-composite")).Select(r => r.Seq).DefaultIfEmpty(0).Min();
            standalone = rows.Where(r => r.Name.StartsWith("Bug4705Standalone")).Select(r => r.Seq).DefaultIfEmpty(0).Max();
            if (highWater > 0 && composite >= highWater && standalone >= highWater) break;
            await Task.Delay(500);
        }

        await daemon.StopAllAsync();

        _output.WriteLine($"=== Version {version}: appended {appended} events, highWater={highWater}, " +
                          $"standalone={standalone}, composite(min)={composite} ===");
        foreach (var (name, seq) in rows) _output.WriteLine($"{seq,6} | {name}");

        // Control sanity: the standalone projection should always reach the high-water.
        standalone.ShouldBe(highWater, $"[v{version}] standalone projection should reach high-water {highWater}");

        // The actual question: does the composite reach the high-water, or stall (e.g. at 1)?
        composite.ShouldBe(highWater,
            $"[v{version}] composite shards stalled at {composite} of high-water {highWater} " +
            $"(rows: {string.Join("; ", rows.Select(r => $"{r.Name}={r.Seq}"))})");
    }

    private static async Task<List<(string Name, long Seq)>> progressionRowsAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select name, coalesce(last_seq_id,0) from {schema}.mt_event_progression order by name");
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, long)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetInt64(1)));
        return rows;
    }
}
