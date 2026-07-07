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
/// marten#4882 (epic jasperfx#486 WS6): the sharded half of <c>daemonload</c>. Pools
/// <c>--tenants</c> tenants across <c>--databases</c> shard databases on the same Postgres server
/// via <c>MultiTenantedWithShardedDatabases</c> (sharded tenancy + per-tenant partitioned events
/// within each shard), runs one projection daemon per shard, appends continuously across every
/// tenant, and samples <c>pg_stat_activity</c> grouped by <c>datname</c>.
///
/// The WS6 assertions this run makes:
/// <list type="bullet">
///   <item><b>O(databases) connections:</b> with the 2.22.0 per-database governors (4 event loads +
///     4 batch writes + HWM + appenders per database), each shard DB's peak should mirror the
///     single-DB daemonload result — <c>--max-connections</c> gates PER DATABASE</item>
///   <item><b>Per-tenant catch-up on every shard:</b> every tenant's per-tenant progression rows
///     reach that tenant's own sequence ceiling in its own shard</item>
///   <item><b>Database-affine placement:</b> each tenant's per-tenant event sequence exists in
///     EXACTLY its assigned shard — no cross-shard bleed</item>
/// </list>
/// </summary>
public sealed partial class DaemonLoadCommand
{
    private const string ShardDatabasePrefix = "scaletest_dl_shard_";

    private async Task<bool> ExecuteShardedAsync(DaemonLoadInput input)
    {
        var totalElapsed = Stopwatch.StartNew();
        var databaseCount = input.DatabasesFlag;
        var shardNames = Enumerable.Range(0, databaseCount)
            .Select(i => $"{ShardDatabasePrefix}{i}")
            .ToArray();

        AnsiConsole.MarkupLine(
            $"[blue]daemonload (sharded): databases=[yellow]{databaseCount}[/] tenants=[yellow]{input.TenantsFlag}[/] " +
            $"projections=[yellow]{input.ProjectionsFlag}[/] duration=[yellow]{input.DurationSecondsFlag}s[/] " +
            $"rate=[yellow]~{input.AppendRatePerSecondFlag}/s[/][/]");

        if (input.WipeFlag)
        {
            await WipeShardedAsync(shardNames).ConfigureAwait(false);
        }

        await EnsureShardDatabasesExistAsync(shardNames).ConfigureAwait(false);

        var shardConnectionStrings = shardNames.ToDictionary(
            name => name,
            name => new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Database = name
            }.ConnectionString);

        var projectionNames = Enumerable.Range(0, Math.Max(1, input.ProjectionsFlag))
            .Select(i => $"LoadRollup{i}")
            .ToArray();

        // ShardedTenancyOptions.ApplicationName stamps every pooled shard connection string, so
        // the by-datname sampler counts exactly the store's connections per database.
        using var store = Marten.DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
                {
                    ApplicationName = ApplicationName
                }.ConnectionString;
                x.SchemaName = Schema;
                x.ApplicationName = ApplicationName;
                x.UseExplicitAssignment();

                foreach (var (name, connectionString) in shardConnectionStrings)
                {
                    x.AddDatabase(name, connectionString);
                }
            });

            opts.DisableNpgsqlLogging = true;
            opts.DatabaseSchemaName = Schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<DaemonLoadEvent>();

            foreach (var name in projectionNames)
            {
                opts.Projections.Add(new DaemonLoadRollupProjection(name), ProjectionLifecycle.Async, name);
            }
        });

        // Apply the schema to every seeded shard database up front — tenant provisioning
        // (per-tenant sequences + partitions) presumes the events schema already exists there.
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var tenants = Enumerable.Range(0, input.TenantsFlag)
            .Select(i => $"tenant_{i:0000}")
            .ToArray();

        // Round-robin, explicit, deterministic placement: tenant i lives on shard i % N. The
        // placement map doubles as the expected-affinity baseline for the bleed check below.
        var tenantsByShard = tenants
            .Select((tenant, i) => (Tenant: tenant, Shard: shardNames[i % databaseCount]))
            .GroupBy(x => x.Shard, x => x.Tenant)
            .ToDictionary(g => g.Key, g => g.ToArray());

        AnsiConsole.MarkupLine(
            $"[grey]Assigning {tenants.Length} tenants round-robin across {databaseCount} shard databases (per-tenant partition DDL — this can take a bit)...[/]");
        for (var i = 0; i < tenants.Length; i++)
        {
            await store.Advanced.AddTenantToShardAsync(tenants[i], shardNames[i % databaseCount], CancellationToken.None)
                .ConfigureAwait(false);
        }

        // One seed event per tenant so every per-tenant sequence + partition is exercised before
        // the daemons start and every (projection × tenant) agent has something to fan out for.
        foreach (var tenant in tenants)
        {
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new DaemonLoadEvent(tenant, 0));
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        using var cts = new CancellationTokenSource();

        // Sample every shard database plus the master (registry) database, one poll session for
        // the whole cluster — pg_stat_activity is cluster-wide.
        var masterDatabaseName = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).Database!;
        var sampledDatabases = shardNames.Append(masterDatabaseName).ToArray();
        await using var sampler = ShardConnectionSampler.Start(
            ConnectionSource.ConnectionString, ApplicationName, sampledDatabases,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)),
            string.IsNullOrWhiteSpace(input.TraceFlag) ? null : input.TraceFlag,
            cts.Token);

        // One daemon per shard database. Under sharded tenancy each shard is its own
        // MartenDatabase and the default-tenant daemon overload is invalid, so address each
        // daemon via a representative tenant assigned to that shard.
        AnsiConsole.MarkupLine("[grey]Starting one projection daemon per shard (per-tenant agent fan-out within each)...[/]");
        var daemons = new List<IProjectionDaemon>();
        try
        {
            foreach (var shard in shardNames)
            {
                var daemon = await store.BuildProjectionDaemonAsync(tenantsByShard[shard][0]).ConfigureAwait(false);
                daemons.Add(daemon);
                await daemon.StartAllAsync().ConfigureAwait(false);
            }

            // ---- Continuous append load ------------------------------------------

            var appended = 0L;
            var appendFailures = 0L;
            var appendElapsed = Stopwatch.StartNew();
            var writers = Enumerable.Range(0, Math.Max(1, input.WritersFlag))
                .Select(w => Task.Run(() => AppendLoopAsync(store, tenants, w, input, cts.Token,
                    () => Interlocked.Increment(ref appended),
                    () => Interlocked.Increment(ref appendFailures))))
                .ToArray();

            await Task.Delay(TimeSpan.FromSeconds(input.DurationSecondsFlag)).ConfigureAwait(false);
            cts.Cancel();
            await Task.WhenAll(writers).ConfigureAwait(false);
            appendElapsed.Stop();

            // ---- Catch-up + placement verification --------------------------------

            AnsiConsole.MarkupLine(
                $"[grey]Appends stopped ({appended:N0} events). Waiting for per-tenant catch-up on every shard...[/]");
            var (caughtUp, stalled) = await WaitForShardedCatchUpAsync(
                shardConnectionStrings, tenantsByShard, projectionNames,
                TimeSpan.FromSeconds(input.CatchUpTimeoutSecondsFlag)).ConfigureAwait(false);

            var placementViolations = await CheckPlacementAffinityAsync(shardConnectionStrings, tenantsByShard)
                .ConfigureAwait(false);

            var perDatabase = sampler.Capture();

            foreach (var daemon in daemons)
            {
                await daemon.StopAllAsync().ConfigureAwait(false);
            }

            totalElapsed.Stop();

            // ---- Report ------------------------------------------------------------

            var appendRate = appended / Math.Max(0.001, appendElapsed.Elapsed.TotalSeconds);
            var shardSnapshots = perDatabase.Where(x => x.Database != masterDatabaseName).ToArray();
            var masterSnapshot = perDatabase.FirstOrDefault(x => x.Database == masterDatabaseName);

            var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
            table.AddRow("Shard databases", databaseCount.ToString("N0"));
            table.AddRow("Tenants", tenants.Length.ToString("N0"));
            table.AddRow("Async projections", projectionNames.Length.ToString("N0"));
            table.AddRow("Tenant agents (projections × tenants)", (tenants.Length * projectionNames.Length).ToString("N0"));
            table.AddRow("Events appended", appended.ToString("N0"));
            table.AddRow("Append failures", appendFailures.ToString("N0"));
            table.AddRow("Sustained append rate (events/sec)", appendRate.ToString("N0"));
            foreach (var snapshot in shardSnapshots)
            {
                table.AddRow($"[bold]{snapshot.Database} peak connections[/]",
                    $"[bold]{snapshot.MaxTotal:N0}[/] (mean {snapshot.MeanTotal:N1}, busy peak {snapshot.MaxBusy:N0})");
            }
            if (masterSnapshot != null)
            {
                table.AddRow("Master DB peak connections",
                    $"{masterSnapshot.MaxTotal:N0} (mean {masterSnapshot.MeanTotal:N1})");
            }
            table.AddRow("Peak across shards", shardSnapshots.Length > 0 ? shardSnapshots.Max(x => x.MaxTotal).ToString("N0") : "0");
            table.AddRow("Total peak (all shards summed)", shardSnapshots.Sum(x => x.MaxTotal).ToString("N0"));
            table.AddRow("Tenants caught up", $"{caughtUp.Count:N0} / {tenants.Length:N0}");
            table.AddRow("Placement violations", placementViolations.Count.ToString("N0"));
            table.AddRow("Total elapsed", $"{totalElapsed.Elapsed.TotalSeconds:N1}s");
            AnsiConsole.Write(table);

            if (stalled.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    $"[red]{stalled.Count} tenant(s) did not catch up within {input.CatchUpTimeoutSecondsFlag}s: " +
                    $"{string.Join(", ", stalled.Take(10))}{(stalled.Count > 10 ? ", ..." : "")}[/]");
            }

            if (placementViolations.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Cross-shard placement bleed: {string.Join("; ", placementViolations.Take(10))}[/]");
            }

            await WriteShardedMetricsAsync(input, databaseCount, tenants.Length, projectionNames.Length,
                appended, appendRate, shardSnapshots, masterSnapshot, caughtUp.Count, stalled,
                placementViolations).ConfigureAwait(false);

            var gatePassed = input.MaxConnectionsFlag <= 0
                             || shardSnapshots.All(x => x.MaxTotal <= input.MaxConnectionsFlag);
            if (!gatePassed)
            {
                var worst = shardSnapshots.MaxBy(x => x.MaxTotal)!;
                AnsiConsole.MarkupLine(
                    $"[red]GATE FAILED: {worst.Database} peak connections {worst.MaxTotal:N0} > --max-connections {input.MaxConnectionsFlag:N0} (per-database gate).[/]");
            }

            var healthy = appendFailures == 0 && stalled.Count == 0 && placementViolations.Count == 0;
            if (!healthy)
            {
                AnsiConsole.MarkupLine("[red]Run unhealthy — see append failures / stalled tenants / placement bleed above.[/]");
            }

            return gatePassed && healthy;
        }
        finally
        {
            foreach (var daemon in daemons)
            {
                daemon.SafeDispose();
            }
        }
    }

    /// <summary>
    /// Wait until, on every shard, every assigned tenant's per-tenant progression rows have
    /// reached that tenant's own sequence ceiling. One catch-up query per shard per poll.
    /// </summary>
    private static async Task<(HashSet<string> CaughtUp, List<string> Stalled)> WaitForShardedCatchUpAsync(
        IReadOnlyDictionary<string, string> shardConnectionStrings,
        IReadOnlyDictionary<string, string[]> tenantsByShard,
        string[] projectionNames,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var caughtUp = new HashSet<string>();
        var stalled = new List<string>();

        while (true)
        {
            caughtUp.Clear();
            stalled = new List<string>();

            foreach (var (shard, connectionString) in shardConnectionStrings)
            {
                var (shardCaughtUp, shardStalled) = await CheckCatchUpOnceAsync(
                    connectionString, Schema, tenantsByShard[shard], projectionNames).ConfigureAwait(false);
                caughtUp.UnionWith(shardCaughtUp);
                stalled.AddRange(shardStalled);
            }

            if (stalled.Count == 0 || DateTime.UtcNow >= deadline)
            {
                return (caughtUp, stalled);
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Database-affine placement sanity: each tenant's per-tenant event sequence must exist in
    /// EXACTLY its assigned shard database. A tenant sequence found on a foreign shard (or missing
    /// from its home shard) is cross-shard bleed.
    /// </summary>
    private static async Task<List<string>> CheckPlacementAffinityAsync(
        IReadOnlyDictionary<string, string> shardConnectionStrings,
        IReadOnlyDictionary<string, string[]> tenantsByShard)
    {
        var violations = new List<string>();

        foreach (var (shard, connectionString) in shardConnectionStrings)
        {
            var found = new HashSet<string>();
            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                await using var cmd = new NpgsqlCommand(
                    "SELECT replace(sequencename, 'mt_events_sequence_', '') FROM pg_sequences " +
                    "WHERE schemaname = @schema AND sequencename LIKE 'mt_events_sequence_%'", conn);
                cmd.Parameters.AddWithValue("schema", Schema);
                await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    found.Add(reader.GetString(0));
                }
            }

            var expected = tenantsByShard[shard].ToHashSet();
            foreach (var missing in expected.Where(t => !found.Contains(t)))
            {
                violations.Add($"{missing} missing from home shard {shard}");
            }

            foreach (var foreign in found.Where(t => !expected.Contains(t)))
            {
                violations.Add($"{foreign} bled onto foreign shard {shard}");
            }
        }

        return violations;
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

        // Registry + assignment tables live in the master database's scaletest schema
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

    private static async Task WriteShardedMetricsAsync(DaemonLoadInput input, int databases, int tenants,
        int projections, long appended, double appendRate,
        IReadOnlyList<ShardConnectionSampler.DatabaseSnapshot> shardSnapshots,
        ShardConnectionSampler.DatabaseSnapshot? masterSnapshot,
        int caughtUpTenants, List<string> stalledTenants, List<string> placementViolations)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "daemonload-sharded",
            databases,
            tenants,
            projections,
            tenantAgents = tenants * projections,
            durationSeconds = input.DurationSecondsFlag,
            eventsAppended = appended,
            appendRatePerSecond = appendRate,
            connectionsPerDatabase = shardSnapshots.ToDictionary(
                x => x.Database,
                x => new
                {
                    samples = x.SampleCount,
                    maxTotal = x.MaxTotal,
                    meanTotal = x.MeanTotal,
                    maxBusy = x.MaxBusy,
                    meanBusy = x.MeanBusy
                }),
            masterDatabase = masterSnapshot == null
                ? null
                : new
                {
                    database = masterSnapshot.Database,
                    maxTotal = masterSnapshot.MaxTotal,
                    meanTotal = masterSnapshot.MeanTotal
                },
            peakAcrossShards = shardSnapshots.Count > 0 ? shardSnapshots.Max(x => x.MaxTotal) : 0,
            totalPeakSummed = shardSnapshots.Sum(x => x.MaxTotal),
            caughtUpTenants,
            stalledTenants,
            placementViolations
        };

        await File.WriteAllTextAsync(input.MetricsFlag,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Metrics written to {input.MetricsFlag}[/]");
    }
}
