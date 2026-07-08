using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.ScaleTesting.Instrumentation;
using Marten.Storage;
using Npgsql;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// <c>rebuildload</c> (marten#4884, epic jasperfx#486 WS6/WS3 closeout): load-test many tenant
/// shards rebuilding concurrently, sweeping the three governor knobs so their shipped defaults can
/// be confirmed — or a change recommended — from evidence.
///
/// For each configuration in the sweep the harness builds a fresh store with the governors applied
/// (<c>MaxConcurrentEventLoadsPerDatabase</c>, <c>MaxConcurrentBatchWritesPerDatabase</c>,
/// <c>EnableExtendedProgressionTracking</c>), then rebuilds every registered projection with the
/// outer rebuild fan-out throttled to the swept cap value — a faithful replica of
/// <c>ProjectionHost.RebuildProjectionsWithCapAsync</c> (jasperfx#463), whose shipped default is
/// <c>max(1, MaxPoolSize / 8)</c>. Throughout, <c>pg_stat_activity</c> and the
/// <c>mt_event_progression</c> lock-wait sampler run, so each row of the comparison carries
/// wall-clock, peak/idle connections and progression contention.
///
/// This is a MEASUREMENT tool: it never changes a shipped default. The recommendation it prints is
/// advisory, to be weighed into <c>rebuilding.md</c> by a human.
/// </summary>
[Description("marten#4884 (epic jasperfx#486 WS6/WS3): rebuild-load governor sweep. Rebuilds N projections × many partitioned tenants under a swept per-database rebuild cap + inner load/write governors, measuring wall-clock, peak/idle connections and mt_event_progression contention per configuration, then prints a tuning recommendation. Never changes a shipped default.", Name = "rebuildload")]
public sealed partial class RebuildLoadCommand: JasperFxAsyncCommand<RebuildLoadInput>
{
    private const string Schema = "scaletest_rebuildload";

    public override async Task<bool> Execute(RebuildLoadInput input)
    {
        if (input.DatabasesFlag > 1)
        {
            // Sharded rebuild sweep: confirm the per-database governors hold when N shard databases
            // rebuild concurrently (each shard is its own governed database). See the partial below.
            return await ExecuteShardedAsync(input).ConfigureAwait(false);
        }

        var caps = ParseInts(input.CapsFlag);
        var loadGovernors = ParseInts(input.LoadGovernorsFlag);
        var writeGovernors = ParseInts(input.WriteGovernorsFlag);
        var extendedModes = input.SweepExtendedProgressionFlag ? new[] { false, true } : new[] { false };

        var projectionNames = Enumerable.Range(0, Math.Max(1, input.ProjectionsFlag))
            .Select(i => $"RebuildRollup{i}")
            .ToArray();
        var tenants = Enumerable.Range(0, input.TenantsFlag).Select(i => $"tenant_{i:0000}").ToArray();

        AnsiConsole.MarkupLine(
            $"[blue]rebuildload: tenants=[yellow]{tenants.Length}[/] projections=[yellow]{projectionNames.Length}[/] " +
            $"events/tenant=[yellow]{input.EventsPerTenantFlag}[/] caps=[yellow]{input.CapsFlag}[/] " +
            $"load-gov=[yellow]{input.LoadGovernorsFlag}[/] write-gov=[yellow]{input.WriteGovernorsFlag}[/] " +
            $"ext-progression-sweep=[yellow]{input.SweepExtendedProgressionFlag}[/][/]");

        if (input.WipeFlag)
        {
            await WipeSchemaAsync().ConfigureAwait(false);
        }

        if (!input.SkipSeedFlag)
        {
            await SeedAsync(projectionNames, tenants, input).ConfigureAwait(false);
        }

        var poolMax = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).MaxPoolSize;
        var shippedDefaultCap = Math.Max(1, poolMax / 8);
        AnsiConsole.MarkupLine(
            $"[grey]Connection pool MaxPoolSize={poolMax} ⇒ shipped default rebuild cap = max(1, {poolMax}/8) = [yellow]{shippedDefaultCap}[/][/]");

        var results = new List<RebuildLoadResult>();

        foreach (var extended in extendedModes)
        foreach (var loadGovernor in loadGovernors)
        foreach (var writeGovernor in writeGovernors)
        foreach (var cap in caps)
        {
            var result = await RunConfigurationAsync(
                projectionNames, tenants, input, cap, loadGovernor, writeGovernor, extended).ConfigureAwait(false);
            results.Add(result);

            AnsiConsole.MarkupLine(
                $"[grey]  cap={cap} load={loadGovernor} write={writeGovernor} ext={extended}: " +
                $"{result.ElapsedSeconds:N1}s, peak conns {result.PeakConnections}, " +
                $"max waiters {result.MaxProgressionWaiters}[/]");
        }

        RenderResults(results, shippedDefaultCap);
        var recommendation = BuildRecommendation(results, shippedDefaultCap, input);
        AnsiConsole.MarkupLine($"[bold green]Recommendation:[/] {recommendation}");

        await WriteMetricsAsync(input, shippedDefaultCap, poolMax, results, recommendation).ConfigureAwait(false);

        return true;
    }

    private static async Task<RebuildLoadResult> RunConfigurationAsync(
        string[] projectionNames, string[] tenants, RebuildLoadInput input,
        int cap, int loadGovernor, int writeGovernor, bool extended)
    {
        var applicationName = $"scaletest-rebuildload-c{cap}-l{loadGovernor}-w{writeGovernor}-e{(extended ? 1 : 0)}";
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;

        using var store = Marten.DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.DisableNpgsqlLogging = true;
            opts.DatabaseSchemaName = Schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.EnableExtendedProgressionTracking = extended;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<DaemonLoadEvent>();

            // The two inner governors size the daemon's per-database load / write semaphores.
            opts.Projections.MaxConcurrentEventLoadsPerDatabase = loadGovernor;
            opts.Projections.MaxConcurrentBatchWritesPerDatabase = writeGovernor;
            // Set the outer cap for descriptor fidelity; concurrency itself is enforced below by a
            // SemaphoreSlim(cap) around the concurrent RebuildProjectionAsync calls — the same
            // shape as ProjectionHost.RebuildProjectionsWithCapAsync (jasperfx#463).
            opts.Projections.MaxConcurrentRebuildsPerDatabase = cap;

            foreach (var name in projectionNames)
            {
                opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
            }
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        using var cts = new CancellationTokenSource();
        await using var connectionSampler = ConnectionSampler.Start(
            ConnectionSource.ConnectionString, applicationName,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)), null, cts.Token);
        await using var lockSampler = ProgressionLockSampler.Start(
            connectionString, TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)), null, cts.Token);

        var shardTimeout = TimeSpan.FromSeconds(input.ShardTimeoutSecondsFlag);
        using var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();

        // Outer rebuild fan-out throttled to `cap` concurrent projection rebuilds — the exact
        // mechanism jasperfx#463 caps in production.
        using var gate = new SemaphoreSlim(Math.Max(1, cap));
        var rebuilds = projectionNames.Select(async name =>
        {
            await gate.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                await daemon.RebuildProjectionAsync(name, shardTimeout, cts.Token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        await Task.WhenAll(rebuilds).ConfigureAwait(false);
        stopwatch.Stop();

        var connections = connectionSampler.Capture();
        var lockWaits = await lockSampler.StopAsync().ConfigureAwait(false);

        var totalEvents = (long)tenants.Length * input.EventsPerTenantFlag;

        return new RebuildLoadResult(
            cap, loadGovernor, writeGovernor, extended,
            stopwatch.Elapsed.TotalSeconds,
            totalEvents / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds),
            connections.MaxTotal,
            connections.MeanTotal,
            connections.MaxBusy,
            lockWaits.MaxConcurrentWaiters,
            lockWaits.MaxSingleWaitMs,
            lockWaits.ObservedWaiterSeconds);
    }

    private static async Task SeedAsync(string[] projectionNames, string[] tenants, RebuildLoadInput input)
    {
        var expectedEvents = (long)tenants.Length * input.EventsPerTenantFlag;

        using var store = Marten.DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DisableNpgsqlLogging = true;
            opts.DatabaseSchemaName = Schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<DaemonLoadEvent>();

            // Projections registered (Async) so the schema matches what the rebuild configs expect,
            // but seeding never runs them.
            foreach (var name in projectionNames)
            {
                opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
            }
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var stats = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
        if (stats.EventCount >= expectedEvents)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Seed idempotent: {stats.EventCount:N0} events already present (≥ {expectedEvents:N0}); skipping.[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[grey]Registering {tenants.Length} tenants + seeding {expectedEvents:N0} events...[/]");
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants).ConfigureAwait(false);

        var perStream = Math.Max(1, input.EventsPerTenantFlag / 20);
        await Parallel.ForEachAsync(tenants,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (tenant, ct) =>
            {
                var remaining = input.EventsPerTenantFlag;
                var sequence = 0;
                while (remaining > 0)
                {
                    var batch = Math.Min(perStream, remaining);
                    await using var session = store.LightweightSession(tenant);
                    var events = Enumerable.Range(0, batch)
                        .Select(_ => new DaemonLoadEvent(tenant, ++sequence))
                        .Cast<object>()
                        .ToArray();
                    session.Events.StartStream(Guid.NewGuid(), events);
                    await session.SaveChangesAsync(ct).ConfigureAwait(false);
                    remaining -= batch;
                }
            }).ConfigureAwait(false);

        var after = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Seed complete: {after.EventCount:N0} events / {after.StreamCount:N0} streams.[/]");
    }

    private static void RenderResults(IReadOnlyList<RebuildLoadResult> results, int shippedDefaultCap)
    {
        var table = new Table().Title("rebuildload governor sweep")
            .AddColumn("cap")
            .AddColumn("load")
            .AddColumn("write")
            .AddColumn("ext")
            .AddColumn(new TableColumn("elapsed s").RightAligned())
            .AddColumn(new TableColumn("events/s").RightAligned())
            .AddColumn(new TableColumn("peak conns").RightAligned())
            .AddColumn(new TableColumn("mean conns").RightAligned())
            .AddColumn(new TableColumn("peak busy").RightAligned())
            .AddColumn(new TableColumn("max waiters").RightAligned())
            .AddColumn(new TableColumn("waiter-s").RightAligned());

        foreach (var r in results)
        {
            var capLabel = r.Cap == shippedDefaultCap ? $"[green]{r.Cap}*[/]" : r.Cap.ToString();
            table.AddRow(
                capLabel,
                r.LoadGovernor.ToString(),
                r.WriteGovernor.ToString(),
                r.ExtendedProgression ? "on" : "off",
                r.ElapsedSeconds.ToString("N1"),
                r.ThroughputEventsPerSecond.ToString("N0"),
                r.PeakConnections.ToString("N0"),
                r.MeanConnections.ToString("N1"),
                r.PeakBusyConnections.ToString("N0"),
                r.MaxProgressionWaiters.ToString("N0"),
                r.ObservedWaiterSeconds.ToString("N1"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]* = shipped default cap for this connection pool[/]");
    }

    private static string BuildRecommendation(
        IReadOnlyList<RebuildLoadResult> results, int shippedDefaultCap, RebuildLoadInput input)
    {
        // Compare only the ext-off, default-governor rows so the cap recommendation isn't confounded.
        var baseline = results
            .Where(r => !r.ExtendedProgression)
            .OrderBy(r => r.ElapsedSeconds)
            .ToArray();
        if (baseline.Length == 0)
        {
            return "no results";
        }

        var fastest = baseline[0];
        var atDefault = baseline.FirstOrDefault(r => r.Cap == shippedDefaultCap);

        var parts = new List<string>();

        if (atDefault == null)
        {
            parts.Add($"shipped default cap {shippedDefaultCap} not in the swept set; add it to compare directly.");
        }
        else
        {
            var speedup = (atDefault.ElapsedSeconds - fastest.ElapsedSeconds) / Math.Max(0.001, atDefault.ElapsedSeconds);
            if (fastest.Cap == atDefault.Cap || speedup < 0.10)
            {
                parts.Add(
                    $"the shipped default cap {shippedDefaultCap} is within 10% of the fastest swept cap " +
                    $"({fastest.Cap} at {fastest.ElapsedSeconds:N1}s vs {atDefault.ElapsedSeconds:N1}s) — CONFIRM the default.");
            }
            else
            {
                parts.Add(
                    $"cap {fastest.Cap} rebuilt {speedup:P0} faster than the shipped default {shippedDefaultCap} " +
                    $"({fastest.ElapsedSeconds:N1}s vs {atDefault.ElapsedSeconds:N1}s) at peak {fastest.PeakConnections} conns — " +
                    "consider raising the default IF the pool headroom holds at production scale.");
            }
        }

        // Two-layer headroom check: inner Block(10) fan-out × outer cap must stay under pool.
        var worstPeak = results.Max(r => r.PeakConnections);
        var poolMax = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).MaxPoolSize;
        parts.Add(
            $"two-layer headroom: worst observed peak {worstPeak} conns vs MaxPoolSize {poolMax} " +
            $"({(worstPeak < poolMax ? "within" : "OVER")} pool).");

        if (input.SweepExtendedProgressionFlag)
        {
            var extPairs = results
                .Where(r => r.ExtendedProgression)
                .Select(on =>
                {
                    var off = results.First(o => !o.ExtendedProgression && o.Cap == on.Cap
                        && o.LoadGovernor == on.LoadGovernor && o.WriteGovernor == on.WriteGovernor);
                    return (on.ElapsedSeconds - off.ElapsedSeconds) / Math.Max(0.001, off.ElapsedSeconds);
                })
                .ToArray();
            if (extPairs.Length > 0)
            {
                parts.Add($"EnableExtendedProgressionTracking cost: {extPairs.Average():P0} mean wall-clock overhead.");
            }
        }

        parts.Add("Local-scale evidence is indicative only; confirm at production pool + event volume before amending rebuilding.md.");
        return string.Join(" ", parts);
    }

    private static async Task WriteMetricsAsync(RebuildLoadInput input, int shippedDefaultCap, int poolMax,
        IReadOnlyList<RebuildLoadResult> results, string recommendation)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "rebuildload",
            tenants = input.TenantsFlag,
            projections = input.ProjectionsFlag,
            eventsPerTenant = input.EventsPerTenantFlag,
            totalEvents = (long)input.TenantsFlag * input.EventsPerTenantFlag,
            maxPoolSize = poolMax,
            shippedDefaultCap,
            configurations = results.Select(r => new
            {
                cap = r.Cap,
                loadGovernor = r.LoadGovernor,
                writeGovernor = r.WriteGovernor,
                extendedProgression = r.ExtendedProgression,
                elapsedSeconds = r.ElapsedSeconds,
                throughputEventsPerSecond = r.ThroughputEventsPerSecond,
                peakConnections = r.PeakConnections,
                meanConnections = r.MeanConnections,
                peakBusyConnections = r.PeakBusyConnections,
                maxProgressionWaiters = r.MaxProgressionWaiters,
                maxSingleWaitMs = r.MaxSingleWaitMs,
                observedWaiterSeconds = r.ObservedWaiterSeconds
            }),
            recommendation
        };

        await File.WriteAllTextAsync(input.MetricsFlag,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Metrics written to {input.MetricsFlag}[/]");
    }

    private static async Task WipeSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var drop = new NpgsqlCommand($"drop schema if exists \"{Schema}\" cascade", conn);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static int[] ParseInts(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();
}

/// <summary>One swept configuration's measured rebuild-load result.</summary>
public sealed record RebuildLoadResult(
    int Cap,
    int LoadGovernor,
    int WriteGovernor,
    bool ExtendedProgression,
    double ElapsedSeconds,
    double ThroughputEventsPerSecond,
    int PeakConnections,
    double MeanConnections,
    int PeakBusyConnections,
    long MaxProgressionWaiters,
    long MaxSingleWaitMs,
    double ObservedWaiterSeconds);
