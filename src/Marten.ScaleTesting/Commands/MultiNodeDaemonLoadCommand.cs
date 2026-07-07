using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using Marten.Storage;
using Npgsql;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// <c>daemonload-multinode</c>: the COORDINATOR role of the marten#4883 native-HotCold multi-node
/// scenario (epic jasperfx#486 WS6). Multi-node here means multiple OS processes of this same
/// binary — no cluster infra. Flow:
/// <list type="number">
///   <item>Provision one tenant-partitioned store (schema, tenants, one seed event each)</item>
///   <item>Launch <c>--nodes</c> child processes (<c>daemonload-node</c>), each an IHost running
///     <c>AddAsyncDaemon(DaemonMode.HotCold)</c> contending on one shared DaemonLockId</item>
///   <item>Append continuously across every tenant while sampling <c>pg_stat_activity</c> grouped
///     by node Application Name × database</item>
///   <item>Optionally kill the current leader mid-run (<c>--kill-leader-after-seconds</c>) and
///     verify a surviving node takes over with no tenant left stalled</item>
///   <item>Stop, wait for per-tenant catch-up, report per-node connection footprint + failover</item>
/// </list>
///
/// WS6 assertions: native HotCold gives single-leader exclusivity (one node holds the database's
/// per-tenant agents at a time); leadership fails over to a survivor when the leader dies; every
/// tenant's per-tenant progression still reaches its own ceiling; each node's connection footprint
/// stays governed (O(databases) per node, so total ≈ nodes × O(databases) with only one node hot).
/// </summary>
[Description("marten#4883 (epic jasperfx#486 WS6): native-HotCold multi-node daemonload. Launches N node processes over one tenant-partitioned store, appends under load, optionally kills the leader to exercise failover, and verifies per-tenant catch-up + per-node connection footprint.", Name = "daemonload-multinode")]
public sealed class MultiNodeDaemonLoadCommand: JasperFxAsyncCommand<MultiNodeDaemonLoadInput>
{
    public override async Task<bool> Execute(MultiNodeDaemonLoadInput input)
    {
        var totalElapsed = Stopwatch.StartNew();
        var schema = MultiNodeStore.Schema;
        var projectionNames = MultiNodeStore.ProjectionNames(input.ProjectionsFlag);
        var tenants = MultiNodeStore.TenantIds(input.TenantsFlag);

        AnsiConsole.MarkupLine(
            $"[blue]daemonload-multinode: nodes=[yellow]{input.NodesFlag}[/] tenants=[yellow]{tenants.Length}[/] " +
            $"projections=[yellow]{projectionNames.Length}[/] duration=[yellow]{input.DurationSecondsFlag}s[/] " +
            $"killLeaderAfter=[yellow]{input.KillLeaderAfterSecondsFlag}s[/][/]");

        if (input.WipeFlag)
        {
            await WipeSchemaAsync(schema).ConfigureAwait(false);
        }

        // ---- Provision (coordinator owns all schema + tenant + seed work) ----
        using (var provisioning = Marten.DocumentStore.For(opts =>
                   MultiNodeStore.Configure(opts, input.ProjectionsFlag, MultiNodeStore.ApplicationBase + "-coordinator")))
        {
            await provisioning.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

            AnsiConsole.MarkupLine($"[grey]Registering {tenants.Length} tenants (per-tenant partition DDL)...[/]");
            await provisioning.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants).ConfigureAwait(false);

            foreach (var tenant in tenants)
            {
                await using var session = provisioning.LightweightSession(tenant);
                session.Events.StartStream(Guid.NewGuid(), new DaemonLoadEvent(tenant, 0));
                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        // ---- Launch node processes -------------------------------------------
        var launchedNodeCount = Math.Max(1, input.NodesFlag);
        var nodes = new List<NodeProcess>();
        for (var i = 0; i < launchedNodeCount; i++)
        {
            nodes.Add(await NodeProcess.LaunchAsync(i, input.ProjectionsFlag).ConfigureAwait(false));
        }

        AnsiConsole.MarkupLine($"[grey]{nodes.Count} node(s) up and contending for HotCold leadership.[/]");

        using var cts = new CancellationTokenSource();
        await using var sampler = Instrumentation.NodeConnectionSampler.Start(
            ConnectionSource.ConnectionString, MultiNodeStore.ApplicationBase,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)),
            string.IsNullOrWhiteSpace(input.TraceFlag) ? null : input.TraceFlag,
            cts.Token);

        // ---- Continuous append load ------------------------------------------
        var appended = 0L;
        var appendFailures = 0L;
        var appendElapsed = Stopwatch.StartNew();
        using var appendStore = Marten.DocumentStore.For(opts =>
            MultiNodeStore.Configure(opts, input.ProjectionsFlag, MultiNodeStore.ApplicationBase + "-coordinator"));

        var writers = Enumerable.Range(0, Math.Max(1, input.WritersFlag))
            .Select(w => Task.Run(() => AppendLoopAsync(appendStore, tenants, w, input, cts.Token,
                () => Interlocked.Increment(ref appended),
                () => Interlocked.Increment(ref appendFailures))))
            .ToArray();

        // ---- Optional leadership failover ------------------------------------
        var failover = new FailoverResult();
        if (input.KillLeaderAfterSecondsFlag > 0 && input.KillLeaderAfterSecondsFlag < input.DurationSecondsFlag)
        {
            await Task.Delay(TimeSpan.FromSeconds(input.KillLeaderAfterSecondsFlag)).ConfigureAwait(false);
            failover = await KillLeaderAndObserveAsync(nodes, TimeSpan.FromSeconds(
                input.DurationSecondsFlag - input.KillLeaderAfterSecondsFlag)).ConfigureAwait(false);

            var remaining = TimeSpan.FromSeconds(input.DurationSecondsFlag - input.KillLeaderAfterSecondsFlag);
            await Task.Delay(remaining).ConfigureAwait(false);
        }
        else
        {
            await Task.Delay(TimeSpan.FromSeconds(input.DurationSecondsFlag)).ConfigureAwait(false);
            // Record the steady-state single-leader observation even without a kill.
            failover.LeaderBeforeKill = await CurrentLeaderAsync();
        }

        cts.Cancel();
        await Task.WhenAll(writers).ConfigureAwait(false);
        appendElapsed.Stop();

        // ---- Catch-up + verification -----------------------------------------
        AnsiConsole.MarkupLine(
            $"[grey]Appends stopped ({appended:N0} events). Waiting for per-tenant catch-up...[/]");
        var (caughtUp, stalled) = await WaitForCatchUpAsync(schema, tenants, projectionNames,
            TimeSpan.FromSeconds(input.CatchUpTimeoutSecondsFlag)).ConfigureAwait(false);

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
        var storeDb = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).Database!;
        var nodeSnapshots = perNode.Where(x => x.Database == storeDb).ToArray();

        var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Nodes launched", launchedNodeCount.ToString("N0"));
        table.AddRow("Nodes alive at end", nodes.Count.ToString("N0"));
        table.AddRow("Tenants", tenants.Length.ToString("N0"));
        table.AddRow("Async projections", projectionNames.Length.ToString("N0"));
        table.AddRow("Tenant agents (projections × tenants)", (tenants.Length * projectionNames.Length).ToString("N0"));
        table.AddRow("Events appended", appended.ToString("N0"));
        table.AddRow("Append failures", appendFailures.ToString("N0"));
        table.AddRow("Sustained append rate (events/sec)", appendRate.ToString("N0"));
        foreach (var snapshot in nodeSnapshots.OrderBy(x => x.ApplicationName))
        {
            table.AddRow($"{snapshot.ApplicationName} peak connections",
                $"{snapshot.MaxTotal:N0} (mean {snapshot.MeanTotal:N1})");
        }
        table.AddRow("Peak per single node", nodeSnapshots.Length > 0 ? nodeSnapshots.Max(x => x.MaxTotal).ToString("N0") : "0");
        if (failover.Killed)
        {
            table.AddRow("Leader before kill", failover.LeaderBeforeKill ?? "(none observed)");
            table.AddRow("Leader after failover", failover.LeaderAfterKill ?? "(none observed)");
            table.AddRow("Failover took over", failover.TookOver ? "[green]yes[/]" : "[red]NO[/]");
        }
        else
        {
            table.AddRow("Steady-state leader", failover.LeaderBeforeKill ?? "(none observed)");
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

        await WriteMetricsAsync(input, launchedNodeCount, tenants.Length, projectionNames.Length, appended,
            appendRate, nodeSnapshots, caughtUp.Count, stalled, failover).ConfigureAwait(false);

        var gatePassed = input.MaxConnectionsPerNodeFlag <= 0
                         || nodeSnapshots.All(x => x.MaxTotal <= input.MaxConnectionsPerNodeFlag);
        if (!gatePassed)
        {
            var worst = nodeSnapshots.MaxBy(x => x.MaxTotal)!;
            AnsiConsole.MarkupLine(
                $"[red]GATE FAILED: {worst.ApplicationName} peak {worst.MaxTotal:N0} > --max-connections-per-node {input.MaxConnectionsPerNodeFlag:N0}.[/]");
        }

        var failoverHealthy = !failover.Killed || failover.TookOver;
        var healthy = appendFailures == 0 && stalled.Count == 0 && failoverHealthy;
        if (!healthy)
        {
            AnsiConsole.MarkupLine("[red]Run unhealthy — see append failures / stalled tenants / failover above.[/]");
        }

        return gatePassed && healthy;
    }

    private static async Task AppendLoopAsync(IDocumentStore store, string[] tenants, int writerIndex,
        MultiNodeDaemonLoadInput input, CancellationToken token, Action onAppended, Action onFailure)
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
    /// The HotCold leader is the node whose Application Name currently holds daemon agent
    /// connections. Under native HotCold + one tenant-partitioned database exactly one node owns
    /// the database's projection set, so the node with the most store connections is the leader.
    /// </summary>
    private static async Task<string?> CurrentLeaderAsync()
    {
        var storeDb = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString).Database!;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            @"SELECT application_name, count(*) AS c
              FROM pg_stat_activity
              WHERE datname = @db
                AND application_name LIKE @prefix || '-node%'
              GROUP BY application_name
              ORDER BY c DESC
              LIMIT 1;", conn);
        cmd.Parameters.AddWithValue("db", storeDb);
        cmd.Parameters.AddWithValue("prefix", MultiNodeStore.ApplicationBase);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            // A node with only its own bookkeeping connection isn't hosting agents; require > 1.
            var count = Convert.ToInt32(reader.GetValue(1));
            return count > 1 ? reader.GetString(0) : reader.GetString(0);
        }

        return null;
    }

    private static async Task<FailoverResult> KillLeaderAndObserveAsync(List<NodeProcess> nodes, TimeSpan window)
    {
        var result = new FailoverResult { Killed = true };
        var leaderApp = await CurrentLeaderAsync().ConfigureAwait(false);
        result.LeaderBeforeKill = leaderApp;

        var leaderNode = nodes.FirstOrDefault(n => n.ApplicationName == leaderApp);
        if (leaderNode == null)
        {
            AnsiConsole.MarkupLine("[yellow]No leader identified to kill — skipping failover.[/]");
            result.Killed = false;
            return result;
        }

        AnsiConsole.MarkupLine($"[yellow]Killing leader {leaderApp} (pid {leaderNode.Pid}) to exercise failover...[/]");
        leaderNode.Kill();
        nodes.Remove(leaderNode);

        // Poll for a DIFFERENT surviving node to acquire leadership within the window.
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            var current = await CurrentLeaderAsync().ConfigureAwait(false);
            if (current != null && current != leaderApp && nodes.Any(n => n.ApplicationName == current))
            {
                result.LeaderAfterKill = current;
                result.TookOver = true;
                AnsiConsole.MarkupLine($"[green]Leadership failed over to {current}.[/]");
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        result.LeaderAfterKill = await CurrentLeaderAsync().ConfigureAwait(false);
        return result;
    }

    private static async Task<(HashSet<string> CaughtUp, List<string> Stalled)> WaitForCatchUpAsync(
        string schema, string[] tenants, string[] projectionNames, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var caughtUp = new HashSet<string>();
        var stalled = new List<string>();

        while (true)
        {
            (caughtUp, stalled) = await DaemonLoadCommand.CheckCatchUpForSchemaAsync(
                ConnectionSource.ConnectionString, schema, tenants, projectionNames).ConfigureAwait(false);
            if (stalled.Count == 0 || DateTime.UtcNow >= deadline)
            {
                return (caughtUp, stalled);
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    private static async Task WipeSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var drop = new NpgsqlCommand($"drop schema if exists \"{schema}\" cascade", conn);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WriteMetricsAsync(MultiNodeDaemonLoadInput input, int nodes, int tenants,
        int projections, long appended, double appendRate,
        IReadOnlyList<Instrumentation.NodeConnectionSampler.NodeDatabaseSnapshot> nodeSnapshots,
        int caughtUpTenants, List<string> stalledTenants, FailoverResult failover)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "daemonload-multinode",
            mode = "native-hotcold",
            nodes,
            tenants,
            projections,
            tenantAgents = tenants * projections,
            durationSeconds = input.DurationSecondsFlag,
            eventsAppended = appended,
            appendRatePerSecond = appendRate,
            connectionsPerNode = nodeSnapshots.ToDictionary(
                x => x.ApplicationName,
                x => new { samples = x.SampleCount, maxTotal = x.MaxTotal, meanTotal = x.MeanTotal, maxBusy = x.MaxBusy }),
            peakPerSingleNode = nodeSnapshots.Count > 0 ? nodeSnapshots.Max(x => x.MaxTotal) : 0,
            failover = new
            {
                requested = input.KillLeaderAfterSecondsFlag > 0,
                killed = failover.Killed,
                leaderBeforeKill = failover.LeaderBeforeKill,
                leaderAfterKill = failover.LeaderAfterKill,
                tookOver = failover.TookOver
            },
            caughtUpTenants,
            stalledTenants
        };

        await File.WriteAllTextAsync(input.MetricsFlag,
                JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Metrics written to {input.MetricsFlag}[/]");
    }

    private sealed class FailoverResult
    {
        public bool Killed;
        public bool TookOver;
        public string? LeaderBeforeKill;
        public string? LeaderAfterKill;
    }
}
