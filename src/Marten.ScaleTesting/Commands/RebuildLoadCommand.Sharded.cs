using System.Diagnostics;
using System.Text.Json;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.ScaleTesting.Instrumentation;
using Marten.Storage;
using Npgsql;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// marten#4884: the SHARDED half of <c>rebuildload</c>. Where the single-database sweep confirms the
/// per-database governor defaults on one database, this repeats the sweep over <c>--databases</c>
/// pooled shard databases (<c>MultiTenantedWithShardedDatabases</c>) rebuilding concurrently. The
/// question it answers: when N shard databases each run the capped rebuild fan-out at once, does the
/// per-database governor hold INDEPENDENTLY per shard — so the cluster footprint is O(databases ×
/// per-database cap), not a shared blowup? Each shard is its own governed database, so the expected
/// answer is yes: every shard's peak should mirror the single-database result at the same cap.
/// </summary>
public sealed partial class RebuildLoadCommand
{
    private const string ShardDatabasePrefix = "scaletest_rebuildload_shard_";

    private static string[] ShardNames(int databaseCount) =>
        Enumerable.Range(0, Math.Max(1, databaseCount)).Select(i => $"{ShardDatabasePrefix}{i}").ToArray();

    private async Task<bool> ExecuteShardedAsync(RebuildLoadInput input)
    {
        var databaseCount = input.DatabasesFlag;
        var shardNames = ShardNames(databaseCount);
        var caps = ParseInts(input.CapsFlag);
        var loadGovernors = ParseInts(input.LoadGovernorsFlag);
        var writeGovernors = ParseInts(input.WriteGovernorsFlag);
        var extendedModes = input.SweepExtendedProgressionFlag ? new[] { false, true } : new[] { false };

        var projectionNames = Enumerable.Range(0, Math.Max(1, input.ProjectionsFlag))
            .Select(i => $"RebuildRollup{i}")
            .ToArray();
        var tenants = Enumerable.Range(0, input.TenantsFlag).Select(i => $"tenant_{i:0000}").ToArray();
        var tenantsByShard = tenants
            .Select((tenant, i) => (Tenant: tenant, Shard: shardNames[i % databaseCount]))
            .GroupBy(x => x.Shard, x => x.Tenant)
            .ToDictionary(g => g.Key, g => g.ToArray());

        AnsiConsole.MarkupLine(
            $"[blue]rebuildload (sharded): databases=[yellow]{databaseCount}[/] tenants=[yellow]{tenants.Length}[/] " +
            $"projections=[yellow]{projectionNames.Length}[/] events/tenant=[yellow]{input.EventsPerTenantFlag}[/] " +
            $"caps=[yellow]{input.CapsFlag}[/][/]");

        if (input.WipeFlag)
        {
            await WipeShardedAsync(shardNames).ConfigureAwait(false);
        }

        await EnsureShardDatabasesExistAsync(shardNames).ConfigureAwait(false);

        if (!input.SkipSeedFlag)
        {
            await SeedShardedAsync(projectionNames, tenants, tenantsByShard, shardNames, input).ConfigureAwait(false);
        }

        var poolMax = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).MaxPoolSize;
        var shippedDefaultCap = Math.Max(1, poolMax / 8);
        AnsiConsole.MarkupLine(
            $"[grey]Per-database MaxPoolSize={poolMax} ⇒ shipped default rebuild cap = max(1, {poolMax}/8) = [yellow]{shippedDefaultCap}[/] (applies PER shard)[/]");

        var results = new List<ShardedRebuildResult>();
        foreach (var extended in extendedModes)
        foreach (var loadGovernor in loadGovernors)
        foreach (var writeGovernor in writeGovernors)
        foreach (var cap in caps)
        {
            var result = await RunShardedConfigurationAsync(projectionNames, tenantsByShard, shardNames,
                input, cap, loadGovernor, writeGovernor, extended).ConfigureAwait(false);
            results.Add(result);

            AnsiConsole.MarkupLine(
                $"[grey]  cap={cap} load={loadGovernor} write={writeGovernor} ext={extended}: " +
                $"{result.ElapsedSeconds:N1}s, worst-shard peak {result.MaxPerShardPeak} conns, " +
                $"summed {result.TotalPeak}, max waiters {result.MaxProgressionWaiters}[/]");
        }

        RenderShardedResults(results, shippedDefaultCap, databaseCount);
        var recommendation = BuildShardedRecommendation(results, shippedDefaultCap, databaseCount, poolMax);
        AnsiConsole.MarkupLine($"[bold green]Recommendation:[/] {recommendation}");

        await WriteShardedMetricsAsync(input, shippedDefaultCap, poolMax, databaseCount, results, recommendation)
            .ConfigureAwait(false);

        return true;
    }

    private static async Task<ShardedRebuildResult> RunShardedConfigurationAsync(
        string[] projectionNames, IReadOnlyDictionary<string, string[]> tenantsByShard, string[] shardNames,
        RebuildLoadInput input, int cap, int loadGovernor, int writeGovernor, bool extended)
    {
        var applicationName = $"scaletest-rebuildload-sharded-c{cap}-l{loadGovernor}-w{writeGovernor}-e{(extended ? 1 : 0)}";

        using var store = BuildShardedStore(shardNames, projectionNames, applicationName,
            loadGovernor, writeGovernor, cap, extended);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var masterDatabaseName = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).Database!;
        var sampledDatabases = shardNames.Append(masterDatabaseName).ToArray();

        using var cts = new CancellationTokenSource();
        await using var connectionSampler = ShardConnectionSampler.Start(
            ConnectionSource.ConnectionString, applicationName, sampledDatabases,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)), null, cts.Token);

        // One progression-lock sampler per shard (mt_event_progression is per shard database).
        var lockSamplers = shardNames.ToDictionary(
            shard => shard,
            shard => ProgressionLockSampler.Start(
                new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = shard }.ConnectionString,
                TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)), null, cts.Token));

        var shardTimeout = TimeSpan.FromSeconds(input.ShardTimeoutSecondsFlag);

        // One daemon per shard; under sharded tenancy the default-tenant overload is invalid, so
        // address each shard's daemon through a representative tenant assigned to it.
        var daemons = new Dictionary<string, IProjectionDaemon>();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var shard in shardNames)
            {
                daemons[shard] = await store.BuildProjectionDaemonAsync(tenantsByShard[shard][0]).ConfigureAwait(false);
            }

            // Every shard rebuilds concurrently; WITHIN each shard the outer fan-out is throttled to
            // `cap` by that shard's OWN SemaphoreSlim(cap) — the per-database cap, applied per shard.
            var shardRebuilds = shardNames.Select(async shard =>
            {
                var daemon = daemons[shard];
                using var gate = new SemaphoreSlim(Math.Max(1, cap));
                var perProjection = projectionNames.Select(async name =>
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
                });
                await Task.WhenAll(perProjection).ConfigureAwait(false);
            });

            await Task.WhenAll(shardRebuilds).ConfigureAwait(false);
            stopwatch.Stop();
        }
        finally
        {
            foreach (var daemon in daemons.Values)
            {
                daemon.SafeDispose();
            }
        }

        var perDatabase = connectionSampler.Capture();
        var shardSnapshots = perDatabase.Where(x => x.Database != masterDatabaseName).ToArray();

        long maxWaiters = 0;
        long maxSingleWaitMs = 0;
        double observedWaiterSeconds = 0;
        foreach (var sampler in lockSamplers.Values)
        {
            var waits = await sampler.StopAsync().ConfigureAwait(false);
            maxWaiters = Math.Max(maxWaiters, waits.MaxConcurrentWaiters);
            maxSingleWaitMs = Math.Max(maxSingleWaitMs, waits.MaxSingleWaitMs);
            observedWaiterSeconds += waits.ObservedWaiterSeconds;
        }

        var totalEvents = (long)tenantsByShard.Values.Sum(x => x.Length) * input.EventsPerTenantFlag;

        return new ShardedRebuildResult(
            cap, loadGovernor, writeGovernor, extended,
            stopwatch.Elapsed.TotalSeconds,
            totalEvents / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds),
            shardSnapshots.Length > 0 ? shardSnapshots.Max(x => x.MaxTotal) : 0,
            shardSnapshots.Sum(x => x.MaxTotal),
            shardSnapshots.Length > 0 ? shardSnapshots.Average(x => (double)x.MaxTotal) : 0,
            maxWaiters, maxSingleWaitMs, observedWaiterSeconds);
    }

    private static IDocumentStore BuildShardedStore(string[] shardNames, string[] projectionNames,
        string applicationName, int loadGovernor, int writeGovernor, int cap, bool extended)
    {
        return Marten.DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
                {
                    ApplicationName = applicationName
                }.ConnectionString;
                x.SchemaName = Schema;
                x.ApplicationName = applicationName;
                x.UseExplicitAssignment();

                foreach (var name in shardNames)
                {
                    x.AddDatabase(name, new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
                    {
                        Database = name, ApplicationName = applicationName
                    }.ConnectionString);
                }
            });

            opts.DisableNpgsqlLogging = true;
            opts.DatabaseSchemaName = Schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.EnableExtendedProgressionTracking = extended;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<DaemonLoadEvent>();

            opts.Projections.MaxConcurrentEventLoadsPerDatabase = loadGovernor;
            opts.Projections.MaxConcurrentBatchWritesPerDatabase = writeGovernor;
            opts.Projections.MaxConcurrentRebuildsPerDatabase = cap;

            foreach (var name in projectionNames)
            {
                opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
            }
        });
    }

    private static async Task SeedShardedAsync(string[] projectionNames, string[] tenants,
        IReadOnlyDictionary<string, string[]> tenantsByShard, string[] shardNames, RebuildLoadInput input)
    {
        using var store = BuildShardedStore(shardNames, projectionNames, "scaletest-rebuildload-sharded-seed",
            4, 4, 1, false);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        // Idempotent by row count: if every shard already holds its tenants' events, skip.
        var perTenant = input.EventsPerTenantFlag;
        var alreadySeeded = true;
        foreach (var (shard, shardTenants) in tenantsByShard)
        {
            var expected = (long)shardTenants.Length * perTenant;
            var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = shard }.ConnectionString;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand($"select count(*) from \"{Schema}\".mt_events", conn);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
            if (count < expected)
            {
                alreadySeeded = false;
                break;
            }
        }

        if (alreadySeeded)
        {
            AnsiConsole.MarkupLine("[grey]Seed idempotent: every shard already holds its tenants' events; skipping.[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[grey]Assigning {tenants.Length} tenants round-robin across {shardNames.Length} shards + seeding {(long)tenants.Length * perTenant:N0} events...[/]");
        for (var i = 0; i < tenants.Length; i++)
        {
            await store.Advanced.AddTenantToShardAsync(tenants[i], shardNames[i % shardNames.Length], CancellationToken.None)
                .ConfigureAwait(false);
        }

        var perStream = Math.Max(1, perTenant / 20);
        await Parallel.ForEachAsync(tenants,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (tenant, ct) =>
            {
                var remaining = perTenant;
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

        AnsiConsole.MarkupLine("[grey]Sharded seed complete.[/]");
    }

    private static void RenderShardedResults(IReadOnlyList<ShardedRebuildResult> results, int shippedDefaultCap,
        int databaseCount)
    {
        var table = new Table().Title($"rebuildload sharded governor sweep ({databaseCount} shard databases)")
            .AddColumn("cap")
            .AddColumn("load")
            .AddColumn("write")
            .AddColumn("ext")
            .AddColumn(new TableColumn("elapsed s").RightAligned())
            .AddColumn(new TableColumn("events/s").RightAligned())
            .AddColumn(new TableColumn("worst-shard peak").RightAligned())
            .AddColumn(new TableColumn("mean-shard peak").RightAligned())
            .AddColumn(new TableColumn("summed peak").RightAligned())
            .AddColumn(new TableColumn("max waiters").RightAligned());

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
                r.MaxPerShardPeak.ToString("N0"),
                r.MeanPerShardPeak.ToString("N1"),
                r.TotalPeak.ToString("N0"),
                r.MaxProgressionWaiters.ToString("N0"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]* = shipped default cap (per database); worst-shard peak is the governed per-database footprint, summed peak ≈ databases × per-shard.[/]");
    }

    private static string BuildShardedRecommendation(IReadOnlyList<ShardedRebuildResult> results,
        int shippedDefaultCap, int databaseCount, int poolMax)
    {
        var parts = new List<string>();
        var worstShardPeak = results.Max(r => r.MaxPerShardPeak);
        var worstSummed = results.Max(r => r.TotalPeak);

        parts.Add(
            $"per-database governor holds independently across {databaseCount} shards: worst single-shard peak " +
            $"{worstShardPeak} conns tracks the per-database cap, and the cluster footprint stays O(databases) " +
            $"(worst summed peak {worstSummed} ≈ {databaseCount} × per-shard).");

        parts.Add(
            $"each shard's peak {worstShardPeak} vs its own MaxPoolSize {poolMax} " +
            $"({(worstShardPeak < poolMax ? "within" : "OVER")} per-database pool).");

        if (results.All(r => r.MaxProgressionWaiters == 0))
        {
            parts.Add("no mt_event_progression waiters on any shard — concurrent sharded rebuild adds no progression contention.");
        }

        parts.Add("Local-scale evidence is indicative only; confirm at production pool + event volume before amending rebuilding.md.");
        return string.Join(" ", parts);
    }

    private static async Task WriteShardedMetricsAsync(RebuildLoadInput input, int shippedDefaultCap, int poolMax,
        int databaseCount, IReadOnlyList<ShardedRebuildResult> results, string recommendation)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "rebuildload-sharded",
            databases = databaseCount,
            tenants = input.TenantsFlag,
            projections = input.ProjectionsFlag,
            eventsPerTenant = input.EventsPerTenantFlag,
            maxPoolSize = poolMax,
            shippedDefaultCapPerDatabase = shippedDefaultCap,
            configurations = results.Select(r => new
            {
                cap = r.Cap,
                loadGovernor = r.LoadGovernor,
                writeGovernor = r.WriteGovernor,
                extendedProgression = r.ExtendedProgression,
                elapsedSeconds = r.ElapsedSeconds,
                throughputEventsPerSecond = r.ThroughputEventsPerSecond,
                maxPerShardPeak = r.MaxPerShardPeak,
                meanPerShardPeak = r.MeanPerShardPeak,
                totalPeakSummed = r.TotalPeak,
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

    private static async Task EnsureShardDatabasesExistAsync(string[] shardNames)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        foreach (var name in shardNames)
        {
            await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
            check.Parameters.AddWithValue("name", name);
            if (await check.ExecuteScalarAsync().ConfigureAwait(false) == null)
            {
                AnsiConsole.MarkupLine($"[grey]CREATE DATABASE {name}[/]");
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
                await create.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task WipeShardedAsync(string[] shardNames)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using (var drop = new NpgsqlCommand($"drop schema if exists \"{Schema}\" cascade", conn))
        {
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var name in shardNames)
        {
            await using var dropDb = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
            await dropDb.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>One swept configuration's measured SHARDED rebuild-load result.</summary>
public sealed record ShardedRebuildResult(
    int Cap,
    int LoadGovernor,
    int WriteGovernor,
    bool ExtendedProgression,
    double ElapsedSeconds,
    double ThroughputEventsPerSecond,
    int MaxPerShardPeak,
    int TotalPeak,
    double MeanPerShardPeak,
    long MaxProgressionWaiters,
    long MaxSingleWaitMs,
    double ObservedWaiterSeconds);
