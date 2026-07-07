using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Instrumentation;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

[Description("Rebuild a registered projection (default: TelehealthComposite) via the single-pass CompositeReplayExecutor. " +
             "Drives the daemon's RebuildProjectionAsync across every shard. Expects events to be already seeded — see the `seed` subcommand.")]
public sealed class RebuildCommand: JasperFxAsyncCommand<RebuildInput>
{
    public override async Task<bool> Execute(RebuildInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore();
        var schemaName = store.Options.DatabaseSchemaName ?? "public";

        // Ensure the schema is in place so a brand-new database can still
        // rebuild even if the user skipped the `seed` step (no events =
        // immediate no-op).
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var stats = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
        AnsiConsole.MarkupLine(
            $"[blue]Rebuilding projection [yellow]{input.ProjectionFlag}[/] against {stats.EventCount:N0} events / {stats.StreamCount:N0} streams.[/]");

        using var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);

        var shardTimeout = TimeSpan.FromSeconds(input.ShardTimeoutSecondsFlag);

        // #4684 Phase E.1: arm the optional harness instrumentation. When --instrument is off
        // (or implied off because no trace path was set), the returned object is a no-op so
        // production-style runs aren't paying for measurement.
        var instrumentationOptions = BuildInstrumentationOptions(input);
        var progressionRow = input.ProjectionFlag + ":All";
        await using var instrumentation = RebuildInstrumentation.Start(
            instrumentationOptions, ConnectionSource.ConnectionString,
            schemaName, progressionRow, CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await daemon.RebuildProjectionAsync(input.ProjectionFlag, shardTimeout, CancellationToken.None)
                .ConfigureAwait(false);
            stopwatch.Stop();
        }
        catch (Exception e)
        {
            stopwatch.Stop();
            AnsiConsole.MarkupLine($"[red]Rebuild FAILED after {stopwatch.Elapsed.TotalSeconds:N1}s.[/]");
            AnsiConsole.WriteException(e);
            return false;
        }

        var snapshot = await instrumentation.CaptureAsync().ConfigureAwait(false);
        var rate = stats.EventCount > 0
            ? stats.EventCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds)
            : 0;

        AnsiConsole.MarkupLine($"[green]Rebuild complete.[/]");
        var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Projection", input.ProjectionFlag);
        table.AddRow("Events processed", stats.EventCount.ToString("N0"));
        table.AddRow("Streams", stats.StreamCount.ToString("N0"));
        table.AddRow("Elapsed", $"{stopwatch.Elapsed.TotalSeconds:N1}s");
        table.AddRow("Throughput (events/sec)", rate.ToString("N0"));
        if (snapshot.Enabled)
        {
            table.AddRow("Throughput p50 (events/sec)", snapshot.Throughput.P50.ToString("N0"));
            table.AddRow("Throughput p95 (events/sec)", snapshot.Throughput.P95.ToString("N0"));
            table.AddRow("Throughput max (events/sec)", snapshot.Throughput.Max.ToString("N0"));
            table.AddRow("Npgsql commands total", snapshot.NpgsqlCommandCount.ToString("N0"));
            table.AddRow("Progress samples", snapshot.Samples.Count.ToString("N0"));
            var lw = snapshot.ProgressionLockWaits;
            table.AddRow("Progression max waiters", lw.MaxConcurrentWaiters.ToString("N0"));
            table.AddRow("Progression max single-wait (ms)", lw.MaxSingleWaitMs.ToString("N0"));
            table.AddRow("Progression observed waiter-seconds", lw.ObservedWaiterSeconds.ToString("N1"));
            var pb = snapshot.PerBatch;
            table.AddRow("Batches observed", pb.BatchCount.ToString("N0"));
            if (pb.BatchCount > 0)
            {
                table.AddRow("Batch p50 total/fetch/group/exec (ms)",
                    $"{pb.P50.TotalMs:N0} / {pb.P50.EventFetchMs:N0} / {pb.P50.GroupingMs:N0} / {pb.P50.ExecutionMs:N0}");
                table.AddRow("Batch p95 total/fetch/group/exec (ms)",
                    $"{pb.P95.TotalMs:N0} / {pb.P95.EventFetchMs:N0} / {pb.P95.GroupingMs:N0} / {pb.P95.ExecutionMs:N0}");
                table.AddRow("Batch p50/p95 DB round-trips", $"{pb.P50.RoundTripCount:N0} / {pb.P95.RoundTripCount:N0}");
            }
            foreach (var (projection, lookups) in snapshot.Lookups.OrderBy(x => x.Key))
            {
                table.AddRow($"Lookups: {projection}",
                    $"{lookups.Lookups:N0} over {lookups.Events:N0} events ({lookups.LookupsPerEvent:N3}/event)");
            }
        }
        AnsiConsole.Write(table);

        await WriteMetricsAsync(input.MetricsFlag, stats, stopwatch.Elapsed, rate, snapshot).ConfigureAwait(false);

        return true;
    }

    private static InstrumentationOptions BuildInstrumentationOptions(RebuildInput input)
    {
        // --instrument-trace and --instrument-lock-trace both imply --instrument so users don't
        // have to remember the master toggle separately.
        var enabled = input.InstrumentFlag
            || !string.IsNullOrWhiteSpace(input.InstrumentTraceFlag)
            || !string.IsNullOrWhiteSpace(input.InstrumentLockTraceFlag)
            || !string.IsNullOrWhiteSpace(input.InstrumentBatchTraceFlag);
        return new InstrumentationOptions
        {
            Enabled = enabled,
            ProgressSampleInterval = TimeSpan.FromSeconds(Math.Max(0.1, input.InstrumentSampleSecondsFlag)),
            TracePath = string.IsNullOrWhiteSpace(input.InstrumentTraceFlag) ? null : input.InstrumentTraceFlag,
            LockTracePath = string.IsNullOrWhiteSpace(input.InstrumentLockTraceFlag) ? null : input.InstrumentLockTraceFlag,
            BatchTracePath = string.IsNullOrWhiteSpace(input.InstrumentBatchTraceFlag) ? null : input.InstrumentBatchTraceFlag
        };
    }

    private static async Task WriteMetricsAsync(string? path, Marten.Events.EventStoreStatistics stats,
        TimeSpan elapsed, double rate, RebuildInstrumentation.Snapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Slim the instrumentation block for JSON -- a multi-hour run's per-sample list would
        // bloat metrics.json into the MB range; the rolled-up percentiles + total are what the
        // dashboard / regression-comparison tooling actually consumes. Per-sample detail lives in
        // the CSV trace when --instrument-trace is set.
        var doc = new
        {
            projection = "rebuild",
            events = stats.EventCount,
            streams = stats.StreamCount,
            elapsedSeconds = elapsed.TotalSeconds,
            throughputEventsPerSecond = rate,
            instrumentation = new
            {
                enabled = snapshot.Enabled,
                sampleCount = snapshot.Samples.Count,
                npgsqlCommandCount = snapshot.NpgsqlCommandCount,
                throughput = snapshot.Throughput,
                progressionLockWaits = snapshot.ProgressionLockWaits,
                perBatch = new
                {
                    batches = snapshot.PerBatch.BatchCount,
                    p50 = snapshot.PerBatch.P50,
                    p95 = snapshot.PerBatch.P95,
                    p99 = snapshot.PerBatch.P99,
                    perShard = snapshot.PerBatch.PerShard
                },
                lookups = snapshot.Lookups
            }
        };
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }))
            .ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[grey]Wrote metrics to {path}[/]");
    }
}
