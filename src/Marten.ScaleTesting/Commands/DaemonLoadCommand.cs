using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.ScaleTesting.Instrumentation;
using Marten.Storage;
using Npgsql;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// <c>daemonload</c>: the jasperfx#486 WS2 measurement scenario. Everything else in this harness
/// measures REBUILDS; this measures the steady-state RUNNING daemon — the deployment shape whose
/// database connection footprint at ~100 tenants is the WS2 concern.
///
/// Flow:
/// <list type="number">
///   <item>Build an isolated <c>UseTenantPartitionedEvents</c> store in its own schema with
///     <c>--projections</c> async projections and a dedicated <c>Application Name</c> so
///     <c>pg_stat_activity</c> can attribute every connection the store opens</item>
///   <item>Register <c>--tenants</c> tenants and start the projection daemon —
///     <c>StartAllAsync</c> fans out one subscription agent per (projection × tenant)</item>
///   <item>Append events continuously across every tenant for <c>--duration-seconds</c> while
///     sampling the store's connection count every <c>--sample-seconds</c></item>
///   <item>Stop appending, wait for every tenant's agents to catch up to that tenant's own
///     high-water ceiling, and report peak/mean connections + catch-up coverage</item>
/// </list>
///
/// The WS2 gate: connections should be O(databases), not O(tenant agents). Run this BEFORE and
/// AFTER the daemon command-batching work lands to quantify the win; <c>--max-connections</c>
/// turns the report into a pass/fail gate for regression runs.
/// </summary>
[Description("WS2 (jasperfx#486): run the async daemon over N partitioned tenants under continuous append load and sample pg_stat_activity for the store's connection footprint.")]
public sealed partial class DaemonLoadCommand: JasperFxAsyncCommand<DaemonLoadInput>
{
    private const string Schema = "scaletest_daemonload";
    private const string ApplicationName = "scaletest-daemonload";

    public override Task<bool> Execute(DaemonLoadInput input)
    {
        // marten#4882: N > 1 pools the tenants across N shard databases (sharded tenancy);
        // the original single-database WS2 scenario is untouched at the default of 1.
        return input.DatabasesFlag > 1
            ? ExecuteShardedAsync(input)
            : ExecuteSingleDatabaseAsync(input);
    }

    private async Task<bool> ExecuteSingleDatabaseAsync(DaemonLoadInput input)
    {
        var totalElapsed = Stopwatch.StartNew();

        AnsiConsole.MarkupLine(
            $"[blue]daemonload: schema=[yellow]{Schema}[/] tenants=[yellow]{input.TenantsFlag}[/] " +
            $"projections=[yellow]{input.ProjectionsFlag}[/] duration=[yellow]{input.DurationSecondsFlag}s[/] " +
            $"rate=[yellow]~{input.AppendRatePerSecondFlag}/s[/][/]");

        if (input.WipeFlag)
        {
            await WipeSchemaAsync().ConfigureAwait(false);
        }

        // Stamp our own Application Name so the sampler counts exactly the store's connections —
        // daemon sessions, event loaders, appender sessions — and nothing else on the dev box.
        var storeConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = ApplicationName
        }.ConnectionString;

        var projectionNames = Enumerable.Range(0, Math.Max(1, input.ProjectionsFlag))
            .Select(i => $"LoadRollup{i}")
            .ToArray();

        using var store = Marten.DocumentStore.For(opts =>
        {
            opts.Connection(storeConnectionString);
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

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var tenants = Enumerable.Range(0, input.TenantsFlag)
            .Select(i => $"tenant_{i:0000}")
            .ToArray();

        AnsiConsole.MarkupLine($"[grey]Registering {tenants.Length} tenants (per-tenant partition DDL — this can take a bit)...[/]");
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants).ConfigureAwait(false);

        // One seed event per tenant so every per-tenant sequence + partition exists before the
        // daemon starts, and every (projection × tenant) agent has something to fan out for.
        foreach (var tenant in tenants)
        {
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream(Guid.NewGuid(), new DaemonLoadEvent(tenant, 0));
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        using var cts = new CancellationTokenSource();
        await using var sampler = ConnectionSampler.Start(
            ConnectionSource.ConnectionString, ApplicationName,
            TimeSpan.FromSeconds(Math.Max(0.1, input.SampleSecondsFlag)),
            string.IsNullOrWhiteSpace(input.TraceFlag) ? null : input.TraceFlag,
            cts.Token);

        AnsiConsole.MarkupLine("[grey]Starting projection daemon (per-tenant agent fan-out)...[/]");
        using var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);
        await daemon.StartAllAsync().ConfigureAwait(false);

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

        // ---- Catch-up + verification ------------------------------------------

        AnsiConsole.MarkupLine(
            $"[grey]Appends stopped ({appended:N0} events). Waiting for per-tenant catch-up...[/]");
        var (caughtUpTenants, stalledTenants) = await WaitForCatchUpAsync(
            tenants, projectionNames, TimeSpan.FromSeconds(input.CatchUpTimeoutSecondsFlag))
            .ConfigureAwait(false);

        var connections = sampler.Capture();
        await daemon.StopAllAsync().ConfigureAwait(false);

        totalElapsed.Stop();

        // ---- Report ------------------------------------------------------------

        var appendRate = appended / Math.Max(0.001, appendElapsed.Elapsed.TotalSeconds);
        var agentCount = tenants.Length * projectionNames.Length;

        var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Tenants", tenants.Length.ToString("N0"));
        table.AddRow("Async projections", projectionNames.Length.ToString("N0"));
        table.AddRow("Tenant agents (projections × tenants)", agentCount.ToString("N0"));
        table.AddRow("Events appended", appended.ToString("N0"));
        table.AddRow("Append failures", appendFailures.ToString("N0"));
        table.AddRow("Sustained append rate (events/sec)", appendRate.ToString("N0"));
        table.AddRow("Connection samples", connections.SampleCount.ToString("N0"));
        table.AddRow("[bold]Peak connections[/]", $"[bold]{connections.MaxTotal:N0}[/]");
        table.AddRow("Mean connections", connections.MeanTotal.ToString("N1"));
        table.AddRow("Peak busy connections", connections.MaxBusy.ToString("N0"));
        table.AddRow("Mean busy connections", connections.MeanBusy.ToString("N1"));
        table.AddRow("Tenants caught up", $"{caughtUpTenants.Count:N0} / {tenants.Length:N0}");
        table.AddRow("Total elapsed", $"{totalElapsed.Elapsed.TotalSeconds:N1}s");
        AnsiConsole.Write(table);

        if (stalledTenants.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[red]{stalledTenants.Count} tenant(s) did not catch up within {input.CatchUpTimeoutSecondsFlag}s: " +
                $"{string.Join(", ", stalledTenants.Take(10))}{(stalledTenants.Count > 10 ? ", ..." : "")}[/]");
        }

        await WriteMetricsAsync(input, tenants.Length, projectionNames.Length, appended, appendRate,
            connections, caughtUpTenants.Count, stalledTenants).ConfigureAwait(false);

        var gatePassed = input.MaxConnectionsFlag <= 0 || connections.MaxTotal <= input.MaxConnectionsFlag;
        if (!gatePassed)
        {
            AnsiConsole.MarkupLine(
                $"[red]GATE FAILED: peak connections {connections.MaxTotal:N0} > --max-connections {input.MaxConnectionsFlag:N0}.[/]");
        }

        var healthy = appendFailures == 0 && stalledTenants.Count == 0;
        if (!healthy)
        {
            AnsiConsole.MarkupLine("[red]Run unhealthy — see append failures / stalled tenants above.[/]");
        }

        return gatePassed && healthy;
    }

    private static async Task AppendLoopAsync(IDocumentStore store, string[] tenants, int writerIndex,
        DaemonLoadInput input, CancellationToken token, Action onAppended, Action onFailure)
    {
        // Each writer owns an interleaved slice of the tenant ring so all writers together cover
        // every tenant. Rate control: each writer targets rate/writers events/sec, appending in
        // small per-tenant batches with a delay computed from the batch size.
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
    /// A tenant is caught up when, for every projection, its <c>{projection}:All:{tenant}</c>
    /// progression row has reached that tenant's own sequence ceiling
    /// (<c>last_value</c> of the per-tenant event sequence).
    /// </summary>
    private static Task<(HashSet<string> CaughtUp, List<string> Stalled)> WaitForCatchUpAsync(
        string[] tenants, string[] projectionNames, TimeSpan timeout)
        => WaitForCatchUpAsync(ConnectionSource.ConnectionString, Schema, tenants, projectionNames, timeout);

    private static async Task<(HashSet<string> CaughtUp, List<string> Stalled)> WaitForCatchUpAsync(
        string connectionString, string schema, string[] tenants, string[] projectionNames, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var stalled = new List<string>();
        var caughtUp = new HashSet<string>();

        while (DateTime.UtcNow < deadline)
        {
            (caughtUp, stalled) = await CheckCatchUpOnceAsync(connectionString, schema, tenants, projectionNames).ConfigureAwait(false);
            if (stalled.Count == 0)
            {
                return (caughtUp, stalled);
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        return (caughtUp, stalled);
    }

    private static async Task<(HashSet<string> CaughtUp, List<string> Stalled)> CheckCatchUpOnceAsync(
        string connectionString, string schema, string[] tenants, string[] projectionNames)
    {
        // One round-trip: per tenant, min progression across the per-tenant projection rows vs the
        // tenant's own sequence ceiling. Sequences are named mt_events_sequence_{tenant} by the
        // per-tenant partitioning machinery.
        const string sql = @"
SELECT s.tenant, coalesce(min(p.last_seq_id), 0) AS floor, max(s.ceiling) AS ceiling
FROM (
    SELECT replace(sequencename, 'mt_events_sequence_', '') AS tenant,
           coalesce(last_value, 0) AS ceiling
    FROM pg_sequences
    WHERE schemaname = @schema AND sequencename LIKE 'mt_events_sequence_%'
) s
LEFT JOIN (
    SELECT split_part(name, ':', 3) AS tenant, name, last_seq_id
    FROM {SCHEMA}.mt_event_progression
    WHERE name LIKE '%:All:%'
) p ON p.tenant = s.tenant
GROUP BY s.tenant;";

        var caughtUp = new HashSet<string>();
        var stalled = new List<string>();
        var expectedRows = projectionNames.Length;

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        // Also require the full per-tenant row COUNT so a tenant whose agents never started (zero
        // progression rows) reads as stalled, not vacuously caught up.
        var perTenantRows = new Dictionary<string, int>();
        await using (var countCmd = new NpgsqlCommand(
                         $"select split_part(name, ':', 3), count(*) from {schema}.mt_event_progression where name like '%:All:%' group by 1",
                         conn))
        await using (var countReader = await countCmd.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await countReader.ReadAsync().ConfigureAwait(false))
            {
                perTenantRows[countReader.GetString(0)] = Convert.ToInt32(countReader.GetValue(1));
            }
        }

        await using var cmd = new NpgsqlCommand(sql.Replace("{SCHEMA}", schema), conn);
        cmd.Parameters.AddWithValue("schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        var seen = new HashSet<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var tenant = reader.GetString(0);
            var floor = Convert.ToInt64(reader.GetValue(1));
            var ceiling = Convert.ToInt64(reader.GetValue(2));
            seen.Add(tenant);

            if (floor >= ceiling && perTenantRows.GetValueOrDefault(tenant) >= expectedRows)
            {
                caughtUp.Add(tenant);
            }
            else
            {
                stalled.Add(tenant);
            }
        }

        // A registered tenant with no per-tenant sequence at all is definitely stalled
        stalled.AddRange(tenants.Where(t => !seen.Contains(t)));

        return (caughtUp, stalled);
    }

    private static async Task WipeSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var drop = new NpgsqlCommand($"drop schema if exists \"{Schema}\" cascade", conn);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task WriteMetricsAsync(DaemonLoadInput input, int tenants, int projections,
        long appended, double appendRate, ConnectionSampler.Snapshot connections,
        int caughtUpTenants, List<string> stalledTenants)
    {
        if (string.IsNullOrWhiteSpace(input.MetricsFlag))
        {
            return;
        }

        var doc = new
        {
            scenario = "daemonload",
            tenants,
            projections,
            tenantAgents = tenants * projections,
            durationSeconds = input.DurationSecondsFlag,
            eventsAppended = appended,
            appendRatePerSecond = appendRate,
            connections = new
            {
                samples = connections.SampleCount,
                maxTotal = connections.MaxTotal,
                meanTotal = connections.MeanTotal,
                maxBusy = connections.MaxBusy,
                meanBusy = connections.MeanBusy
            },
            caughtUpTenants,
            stalledTenants
        };

        await File.WriteAllTextAsync(input.MetricsFlag,
            JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Metrics written to {input.MetricsFlag}[/]");
    }
}

public record DaemonLoadEvent(string Tenant, int Sequence);

/// <summary>
/// Deliberately minimal async projection — one doc write per batch. The WS2 measurement targets
/// the DAEMON's connection behavior (event loaders, progression flushes, high-water polling), so
/// the projection body stays cheap to keep projection CPU out of the signal.
/// </summary>
public class DaemonLoadRollup
{
    public string Id { get; set; } = null!;
    public long EventCount { get; set; }
}

public class DaemonLoadRollupProjection: IProjection
{
    private readonly string _name;

    public DaemonLoadRollupProjection(string name)
    {
        _name = name;
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        var count = events.Count(e => e.Data is DaemonLoadEvent);
        if (count == 0)
        {
            return;
        }

        var rollup = await operations.LoadAsync<DaemonLoadRollup>(_name, cancellation)
                     ?? new DaemonLoadRollup { Id = _name };
        rollup.EventCount += count;
        operations.Store(rollup);
    }
}
