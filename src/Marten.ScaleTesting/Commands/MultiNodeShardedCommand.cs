using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Instrumentation;
using Npgsql;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// <c>daemonload-multinode-sharded</c>: the COORDINATOR role of the marten#4883 native-HotCold
/// SHARDED multi-node scenario (epic jasperfx#486 WS6). Where <c>daemonload-multinode</c> runs one
/// tenant-partitioned database (so HotCold gives a single leader + failover), this pools tenants
/// across <c>--databases</c> shard databases via <c>MultiTenantedWithShardedDatabases</c>. Each
/// shard database is its own leadership lock, so different nodes lead different shards — the
/// cross-node agent distribution the epic's topology matrix calls for. Flow:
/// <list type="number">
///   <item>Create <c>--databases</c> shard DBs; provision the sharded store; pool <c>--tenants</c>
///     tenants round-robin across the shards (per-tenant partition DDL + one seed event each)</item>
///   <item>Launch <c>--nodes</c> child processes (<c>daemonload-node --databases N</c>), each an
///     IHost running <c>AddAsyncDaemon(DaemonMode.HotCold)</c> over the same sharded store</item>
///   <item>Append continuously across every tenant while sampling <c>pg_stat_activity</c> grouped by
///     node Application Name × shard database</item>
///   <item>Observe which node leads which shard (distribution); optionally kill a node that leads a
///     shard (<c>--kill-node-after-seconds</c>) and verify its shards redistribute to survivors</item>
///   <item>Stop, wait for per-tenant catch-up on every shard, report distribution + per-node×shard
///     connection footprint + redistribution</item>
/// </list>
///
/// WS6 assertions: HotCold spreads shard leadership across nodes; killing a node redistributes its
/// shards with no tenant left stalled; every tenant's per-tenant progression reaches its own ceiling
/// on its home shard; each node's per-shard connection footprint stays governed (O(databases-led)).
/// </summary>
[Description("marten#4883 (epic jasperfx#486 WS6): native-HotCold SHARDED multi-node daemonload. Pools tenants across N shard databases and launches M node processes, so HotCold distributes shard leadership across nodes; appends under load, optionally kills a node to exercise shard redistribution, and verifies per-tenant catch-up + per-node×shard connection footprint.", Name = "daemonload-multinode-sharded")]
public sealed class MultiNodeShardedCommand: JasperFxAsyncCommand<MultiNodeShardedInput>
{
    public override async Task<bool> Execute(MultiNodeShardedInput input)
    {
        var totalElapsed = Stopwatch.StartNew();
        var databaseCount = Math.Max(1, input.DatabasesFlag);
        var shardNames = MultiNodeStore.ShardDatabaseNames(databaseCount);
        var projectionNames = MultiNodeStore.ProjectionNames(input.ProjectionsFlag);
        var tenants = MultiNodeStore.TenantIds(input.TenantsFlag);
        var nodeCount = Math.Max(1, input.NodesFlag);

        AnsiConsole.MarkupLine(
            $"[blue]daemonload-multinode-sharded: nodes=[yellow]{nodeCount}[/] databases=[yellow]{databaseCount}[/] " +
            $"tenants=[yellow]{tenants.Length}[/] projections=[yellow]{projectionNames.Length}[/] " +
            $"duration=[yellow]{input.DurationSecondsFlag}s[/] killNodeAfter=[yellow]{input.KillNodeAfterSecondsFlag}s[/][/]");

        if (input.WipeFlag)
        {
            await WipeAsync(shardNames).ConfigureAwait(false);
        }

        await EnsureShardDatabasesExistAsync(shardNames).ConfigureAwait(false);

        // Round-robin, deterministic placement: tenant i lives on shard i % N.
        var tenantsByShard = tenants
            .Select((tenant, i) => (Tenant: tenant, Shard: shardNames[i % databaseCount]))
            .GroupBy(x => x.Shard, x => x.Tenant)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var shardConnectionStrings = shardNames.ToDictionary(
            name => name,
            name => new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = name }.ConnectionString);

        // ---- Provision (coordinator owns all schema + tenant + seed work) ----
        using (var provisioning = Marten.DocumentStore.For(opts =>
                   MultiNodeStore.ConfigureSharded(opts, input.ProjectionsFlag, databaseCount,
                       MultiNodeStore.ApplicationBase + "-coordinator")))
        {
            await provisioning.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

            AnsiConsole.MarkupLine(
                $"[grey]Assigning {tenants.Length} tenants round-robin across {databaseCount} shard databases (per-tenant partition DDL — this can take a bit)...[/]");
            for (var i = 0; i < tenants.Length; i++)
            {
                await provisioning.Advanced
                    .AddTenantToShardAsync(tenants[i], shardNames[i % databaseCount], CancellationToken.None)
                    .ConfigureAwait(false);
            }

            foreach (var tenant in tenants)
            {
                await using var session = provisioning.LightweightSession(tenant);
                session.Events.StartStream(Guid.NewGuid(), new DaemonLoadEvent(tenant, 0));
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        // ---- Launch node processes -------------------------------------------
        var nodes = new List<NodeProcess>();
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(await NodeProcess.LaunchAsync(i, input.ProjectionsFlag, databaseCount).ConfigureAwait(false));
        }

        AnsiConsole.MarkupLine($"[grey]{nodes.Count} node(s) up and contending for per-shard HotCold leadership.[/]");

        using var cts = new CancellationTokenSource();
        await using var sampler = NodeConnectionSampler.Start(
            ConnectionSource.ConnectionString, MultiNodeStore.ApplicationBase,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)),
            string.IsNullOrWhiteSpace(input.TraceFlag) ? null : input.TraceFlag,
            cts.Token);

        // ---- Continuous append load ------------------------------------------
        var appended = 0L;
        var appendFailures = 0L;
        var appendElapsed = Stopwatch.StartNew();
        using var appendStore = Marten.DocumentStore.For(opts =>
            MultiNodeStore.ConfigureSharded(opts, input.ProjectionsFlag, databaseCount,
                MultiNodeStore.ApplicationBase + "-coordinator"));

        var writers = Enumerable.Range(0, Math.Max(1, input.WritersFlag))
            .Select(w => Task.Run(() => AppendLoopAsync(appendStore, tenants, w, input, cts.Token,
                () => Interlocked.Increment(ref appended),
                () => Interlocked.Increment(ref appendFailures))))
            .ToArray();

        // Give leadership a moment to settle, then record the steady-state shard→node distribution.
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, input.DurationSecondsFlag / 2.0))).ConfigureAwait(false);
        var distribution = await ShardLeadersAsync(shardNames).ConfigureAwait(false);

        // ---- Optional node kill → shard redistribution -----------------------
        var redistribution = new RedistributionResult();
        var elapsedIntoRun = TimeSpan.FromSeconds(Math.Min(10, input.DurationSecondsFlag / 2.0));
        if (input.KillNodeAfterSecondsFlag > 0 && input.KillNodeAfterSecondsFlag < input.DurationSecondsFlag)
        {
            var untilKill = TimeSpan.FromSeconds(input.KillNodeAfterSecondsFlag) - elapsedIntoRun;
            if (untilKill > TimeSpan.Zero)
            {
                await Task.Delay(untilKill).ConfigureAwait(false);
            }

            redistribution = await KillNodeAndObserveRedistributionAsync(nodes, shardNames,
                TimeSpan.FromSeconds(Math.Max(15, input.DurationSecondsFlag - input.KillNodeAfterSecondsFlag)))
                .ConfigureAwait(false);

            var remaining = TimeSpan.FromSeconds(input.DurationSecondsFlag) - TimeSpan.FromSeconds(input.KillNodeAfterSecondsFlag);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining).ConfigureAwait(false);
            }
        }
        else
        {
            var remaining = TimeSpan.FromSeconds(input.DurationSecondsFlag) - elapsedIntoRun;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining).ConfigureAwait(false);
            }
        }

        cts.Cancel();
        await Task.WhenAll(writers).ConfigureAwait(false);
        appendElapsed.Stop();

        // ---- Catch-up + verification -----------------------------------------
        AnsiConsole.MarkupLine(
            $"[grey]Appends stopped ({appended:N0} events). Waiting for per-tenant catch-up on every shard...[/]");
        var (caughtUp, stalled) = await WaitForShardedCatchUpAsync(shardConnectionStrings, tenantsByShard,
            projectionNames, TimeSpan.FromSeconds(input.CatchUpTimeoutSecondsFlag)).ConfigureAwait(false);

        var perNode = sampler.Capture();

        // ---- Graceful node shutdown ------------------------------------------
        foreach (var node in nodes)
        {
            node.RequestStop();
        }
        foreach (var node in nodes)
        {
            await node.WaitForExitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        totalElapsed.Stop();

        // ---- Report ----------------------------------------------------------
        var appendRate = appended / Math.Max(0.001, appendElapsed.Elapsed.TotalSeconds);
        // Only per-node × shard-database snapshots (exclude the coordinator app and the master DB).
        var shardSet = shardNames.ToHashSet();
        var nodeShardSnapshots = perNode
            .Where(x => x.ApplicationName.Contains("-node") && shardSet.Contains(x.Database))
            .ToArray();
        var distinctLeaderNodes = distribution.Values.Where(v => v != null).Distinct().Count();

        var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Nodes launched", nodeCount.ToString("N0"));
        table.AddRow("Shard databases", databaseCount.ToString("N0"));
        table.AddRow("Tenants", tenants.Length.ToString("N0"));
        table.AddRow("Async projections", projectionNames.Length.ToString("N0"));
        table.AddRow("Tenant agents (projections × tenants)", (tenants.Length * projectionNames.Length).ToString("N0"));
        table.AddRow("Events appended", appended.ToString("N0"));
        table.AddRow("Append failures", appendFailures.ToString("N0"));
        table.AddRow("Sustained append rate (events/sec)", appendRate.ToString("N0"));
        foreach (var shard in shardNames)
        {
            var leader = distribution.TryGetValue(shard, out var l) ? l : null;
            table.AddRow($"Leader of {shard}", leader ?? "[grey](none observed)[/]");
        }
        table.AddRow("Distinct leader nodes (distribution)",
            $"{distinctLeaderNodes:N0} / {Math.Min(nodeCount, databaseCount):N0} possible");
        foreach (var snapshot in nodeShardSnapshots.OrderBy(x => x.ApplicationName).ThenBy(x => x.Database))
        {
            table.AddRow($"{snapshot.ApplicationName} @ {snapshot.Database} peak conns",
                $"{snapshot.MaxTotal:N0} (mean {snapshot.MeanTotal:N1})");
        }
        table.AddRow("Peak per single node×shard",
            nodeShardSnapshots.Length > 0 ? nodeShardSnapshots.Max(x => x.MaxTotal).ToString("N0") : "0");
        if (redistribution.Killed)
        {
            table.AddRow("Killed node", redistribution.KilledNode ?? "(none)");
            table.AddRow("Shards it led", redistribution.OrphanedShards.Count > 0
                ? string.Join(", ", redistribution.OrphanedShards) : "(none)");
            table.AddRow("Shards redistributed", redistribution.Redistributed ? "[green]yes[/]" : "[red]NO[/]");
        }
        table.AddRow("Tenants caught up", $"{caughtUp.Count:N0} / {tenants.Length:N0}");
        table.AddRow("Total elapsed", $"{totalElapsed.Elapsed.TotalSeconds:N1}s");
        AnsiConsole.Write(table);

        if (stalled.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[red]{stalled.Count} tenant(s) did not catch up within {input.CatchUpTimeoutSecondsFlag}s: " +
                $"{string.Join(", ", stalled.Take(10))}{(stalled.Count > 10 ? ", ..." : "")}[/]");
        }

        await WriteMetricsAsync(input, nodeCount, databaseCount, tenants.Length, projectionNames.Length,
            appended, appendRate, distribution, distinctLeaderNodes, nodeShardSnapshots, caughtUp.Count,
            stalled, redistribution).ConfigureAwait(false);

        // ---- Gates -----------------------------------------------------------
        var connectionGatePassed = input.MaxConnectionsPerNodeShardFlag <= 0
                                   || nodeShardSnapshots.All(x => x.MaxTotal <= input.MaxConnectionsPerNodeShardFlag);
        if (!connectionGatePassed)
        {
            var worst = nodeShardSnapshots.MaxBy(x => x.MaxTotal)!;
            AnsiConsole.MarkupLine(
                $"[red]GATE FAILED: {worst.ApplicationName} @ {worst.Database} peak {worst.MaxTotal:N0} > " +
                $"--max-connections-per-node-shard {input.MaxConnectionsPerNodeShardFlag:N0}.[/]");
        }

        // Distribution is a soft check by default (a fast node can transiently grab every shard).
        var distributionExpected = nodeCount >= 2 && databaseCount >= 2;
        var distributionPassed = !input.RequireDistributionFlag || !distributionExpected || distinctLeaderNodes >= 2;
        if (!distributionPassed)
        {
            AnsiConsole.MarkupLine(
                $"[red]GATE FAILED: shard leadership did not span >= 2 nodes ({distinctLeaderNodes} distinct leader node(s)).[/]");
        }
        else if (distributionExpected && distinctLeaderNodes < 2)
        {
            AnsiConsole.MarkupLine(
                "[yellow]NOTE: all shards were led by a single node at the sampled instant — leadership did not spread across nodes this run.[/]");
        }

        var redistributionHealthy = !redistribution.Killed || redistribution.Redistributed;
        var healthy = appendFailures == 0 && stalled.Count == 0 && redistributionHealthy;
        if (!healthy)
        {
            AnsiConsole.MarkupLine("[red]Run unhealthy — see append failures / stalled tenants / redistribution above.[/]");
        }

        return connectionGatePassed && distributionPassed && healthy;
    }

    private static async Task AppendLoopAsync(IDocumentStore store, string[] tenants, int writerIndex,
        MultiNodeShardedInput input, CancellationToken token, Action onAppended, Action onFailure)
    {
        var writerCount = Math.Max(1, input.WritersFlag);
        var perWriterRate = Math.Max(1.0, (double)input.AppendRatePerSecondFlag / writerCount);
        const int batchSize = 5;
        var delay = TimeSpan.FromSeconds(batchSize / perWriterRate);

        var sequence = 0;
        var position = writerIndex;
        while (!token.IsCancellationRequested)
        {
            var tenant = tenants[position % tenants.Length];
            position += writerCount;

            try
            {
                await using var session = store.LightweightSession(tenant);
                var events = Enumerable.Range(0, batchSize)
                    .Select(_ => new DaemonLoadEvent(tenant, ++sequence))
                    .Cast<object>()
                    .ToArray();
                session.Events.StartStream(Guid.NewGuid(), events);
                await session.SaveChangesAsync(token).ConfigureAwait(false);
                for (var i = 0; i < batchSize; i++)
                {
                    onAppended();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                onFailure();
            }

            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// For each shard database, the node whose Application Name currently holds the most connections
    /// on that database is its HotCold leader (a leader hosts that shard's per-tenant projection
    /// agents; non-leaders don't connect to shards they don't lead). Returns shard → leader node app
    /// name (null if no node has claimed the shard yet).
    /// </summary>
    private static async Task<Dictionary<string, string?>> ShardLeadersAsync(string[] shardNames)
    {
        var leaders = shardNames.ToDictionary(s => s, _ => (string?)null);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            @"SELECT datname, application_name, count(*) AS c
              FROM pg_stat_activity
              WHERE datname = ANY(@shards)
                AND application_name LIKE @prefix || '-node%'
              GROUP BY datname, application_name;", conn);
        cmd.Parameters.AddWithValue("shards", shardNames);
        cmd.Parameters.AddWithValue("prefix", MultiNodeStore.ApplicationBase);

        var byShard = new Dictionary<string, (string App, int Count)>();
        await using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                var db = reader.GetString(0);
                var app = reader.GetString(1);
                var count = Convert.ToInt32(reader.GetValue(2));
                if (!byShard.TryGetValue(db, out var current) || count > current.Count)
                {
                    byShard[db] = (app, count);
                }
            }
        }

        foreach (var (shard, winner) in byShard)
        {
            leaders[shard] = winner.App;
        }

        return leaders;
    }

    private static async Task<RedistributionResult> KillNodeAndObserveRedistributionAsync(
        List<NodeProcess> nodes, string[] shardNames, TimeSpan window)
    {
        var result = new RedistributionResult { Killed = true };

        var leadersBefore = await ShardLeadersAsync(shardNames).ConfigureAwait(false);
        // Pick a node that currently leads at least one shard.
        var victimApp = leadersBefore.Values.FirstOrDefault(v => v != null
            && nodes.Any(n => n.ApplicationName == v));
        var victim = nodes.FirstOrDefault(n => n.ApplicationName == victimApp);
        if (victim == null)
        {
            AnsiConsole.MarkupLine("[yellow]No node leads a shard yet — skipping the kill.[/]");
            result.Killed = false;
            return result;
        }

        result.KilledNode = victim.ApplicationName;
        result.OrphanedShards = leadersBefore
            .Where(kv => kv.Value == victim.ApplicationName)
            .Select(kv => kv.Key)
            .ToList();

        AnsiConsole.MarkupLine(
            $"[yellow]Killing {victim.ApplicationName} (pid {victim.Pid}), which leads {result.OrphanedShards.Count} shard(s): " +
            $"{string.Join(", ", result.OrphanedShards)}...[/]");
        victim.Kill();
        nodes.Remove(victim);

        // Poll until every orphaned shard has a NEW leader among the surviving nodes.
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var leadersNow = await ShardLeadersAsync(shardNames).ConfigureAwait(false);
            var allReassigned = result.OrphanedShards.All(shard =>
                leadersNow.TryGetValue(shard, out var leader)
                && leader != null
                && leader != victim.ApplicationName
                && nodes.Any(n => n.ApplicationName == leader));

            if (allReassigned)
            {
                result.Redistributed = true;
                result.LeadersAfter = result.OrphanedShards.ToDictionary(s => s, s => leadersNow[s]);
                AnsiConsole.MarkupLine("[green]All orphaned shards were redistributed to surviving nodes.[/]");
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        var final = await ShardLeadersAsync(shardNames).ConfigureAwait(false);
        result.LeadersAfter = result.OrphanedShards.ToDictionary(s => s, s => final.GetValueOrDefault(s));
        return result;
    }

    private static async Task<(HashSet<string> CaughtUp, List<string> Stalled)> WaitForShardedCatchUpAsync(
        IReadOnlyDictionary<string, string> shardConnectionStrings,
        IReadOnlyDictionary<string, string[]> tenantsByShard,
        string[] projectionNames, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        HashSet<string> caughtUp;
        List<string> stalled;

        while (true)
        {
            caughtUp = new HashSet<string>();
            stalled = new List<string>();

            foreach (var (shard, connectionString) in shardConnectionStrings)
            {
                var (shardCaughtUp, shardStalled) = await DaemonLoadCommand.CheckCatchUpForSchemaAsync(
                    connectionString, MultiNodeStore.Schema, tenantsByShard[shard], projectionNames)
                    .ConfigureAwait(false);
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

    private static async Task WipeAsync(string[] shardNames)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using (var drop = new NpgsqlCommand($"drop schema if exists \"{MultiNodeStore.Schema}\" cascade", conn))
        {
            await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var name in shardNames)
        {
            await using var dropDb = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
            await dropDb.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteMetricsAsync(MultiNodeShardedInput input, int nodes, int databases,
        int tenants, int projections, long appended, double appendRate,
        IReadOnlyDictionary<string, string?> distribution, int distinctLeaderNodes,
        IReadOnlyList<NodeConnectionSampler.NodeDatabaseSnapshot> nodeShardSnapshots,
        int caughtUpTenants, List<string> stalledTenants, RedistributionResult redistribution)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "daemonload-multinode-sharded",
            mode = "native-hotcold-sharded",
            nodes,
            databases,
            tenants,
            projections,
            tenantAgents = tenants * projections,
            durationSeconds = input.DurationSecondsFlag,
            eventsAppended = appended,
            appendRatePerSecond = appendRate,
            shardLeaders = distribution.ToDictionary(kv => kv.Key, kv => kv.Value),
            distinctLeaderNodes,
            connectionsPerNodeShard = nodeShardSnapshots.ToDictionary(
                x => $"{x.ApplicationName}@{x.Database}",
                x => new { samples = x.SampleCount, maxTotal = x.MaxTotal, meanTotal = x.MeanTotal, maxBusy = x.MaxBusy }),
            peakPerSingleNodeShard = nodeShardSnapshots.Count > 0 ? nodeShardSnapshots.Max(x => x.MaxTotal) : 0,
            redistribution = new
            {
                requested = input.KillNodeAfterSecondsFlag > 0,
                killed = redistribution.Killed,
                killedNode = redistribution.KilledNode,
                orphanedShards = redistribution.OrphanedShards,
                redistributed = redistribution.Redistributed,
                leadersAfter = redistribution.LeadersAfter
            },
            caughtUpTenants,
            stalledTenants
        };

        await File.WriteAllTextAsync(input.MetricsFlag,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Metrics written to {input.MetricsFlag}[/]");
    }

    private sealed class RedistributionResult
    {
        public bool Killed;
        public bool Redistributed;
        public string? KilledNode;
        public List<string> OrphanedShards = new();
        public Dictionary<string, string?>? LeadersAfter;
    }
}
