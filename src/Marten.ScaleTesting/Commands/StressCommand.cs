using System.Diagnostics;
using System.Text.Json;
using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Instrumentation;
using Marten.ScaleTesting.Seeding;
using Marten.ScaleTesting.Validation;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

[Description("Chained stress run: seed → rebuild → validate. Single CLI invocation runs the full crash-gate cycle. " +
             "Each phase fails-fast: if seed throws, rebuild + validate are skipped; if rebuild throws, validate is skipped. " +
             "Exit code reflects the worst phase. The actual crash-free gate for #4667 verification.")]
public sealed class StressCommand: JasperFxAsyncCommand<StressInput>
{
    public override async Task<bool> Execute(StressInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore();
        var schemaName = store.Options.DatabaseSchemaName ?? "public";

        var totalStopwatch = Stopwatch.StartNew();
        var summary = new List<(string Phase, string Status, TimeSpan Elapsed, string Note)>();

        // ---- Phase 1: seed ------------------------------------------------

        if (input.WipeFlag)
        {
            AnsiConsole.MarkupLine("[red]--wipe specified — destroying all data before seeding.[/]");
            await store.Advanced.Clean.CompletelyRemoveAllAsync().ConfigureAwait(false);
        }
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        AnsiConsole.MarkupLine($"[blue]Phase 1: seed[/]");
        var seedSw = Stopwatch.StartNew();
        SeedReport seedReport;
        try
        {
            var seeder = new EventSeeder(store, input.ToSeedOptions());
            seedReport = await seeder.RunAsync(CancellationToken.None).ConfigureAwait(false);
            seedSw.Stop();
        }
        catch (Exception e)
        {
            seedSw.Stop();
            summary.Add(("seed", "FAILED", seedSw.Elapsed, e.GetType().Name + ": " + e.Message));
            AnsiConsole.MarkupLine($"[red]Seed FAILED after {seedSw.Elapsed.TotalSeconds:N1}s — skipping rebuild + validate.[/]");
            AnsiConsole.WriteException(e);
            PrintSummary(summary, totalStopwatch);
            return false;
        }

        summary.Add((
            "seed",
            seedReport.AlreadySeeded ? "SKIPPED" : "OK",
            seedSw.Elapsed,
            seedReport.AlreadySeeded
                ? "tenants already meet the target event count"
                : $"{seedReport.Events:N0} events / {seedReport.Batches:N0} batches"));

        // ---- Phase 2: rebuild --------------------------------------------

        AnsiConsole.MarkupLine($"[blue]Phase 2: rebuild [yellow]{input.ProjectionFlag}[/][/]");

        // #4684 Phase E.1: arm harness instrumentation around the rebuild only. Seed phase isn't
        // measured by the same machinery -- it's a bulk-writer path that exercises a different
        // bottleneck and gets its own per-batch console reporting already.
        var instrumentationOptions = BuildInstrumentationOptions(input);
        var progressionRow = input.ProjectionFlag + ":All";
        await using var instrumentation = RebuildInstrumentation.Start(
            instrumentationOptions, ConnectionSource.ConnectionString,
            schemaName, progressionRow, CancellationToken.None);

        var rebuildSw = Stopwatch.StartNew();
        var rebuildEventCount = 0L;
        RebuildInstrumentation.Snapshot rebuildSnapshot = RebuildInstrumentation.Snapshot.Disabled;
        try
        {
            var stats = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
            rebuildEventCount = stats.EventCount;

            using var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);
            await daemon.RebuildProjectionAsync(
                input.ProjectionFlag,
                TimeSpan.FromSeconds(input.ShardTimeoutSecondsFlag),
                CancellationToken.None).ConfigureAwait(false);
            rebuildSw.Stop();
            rebuildSnapshot = await instrumentation.CaptureAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            rebuildSw.Stop();
            // Capture whatever the sampler observed before the crash; useful for post-mortem.
            try { rebuildSnapshot = await instrumentation.CaptureAsync().ConfigureAwait(false); } catch { /* shutdown */ }
            summary.Add(("rebuild", "FAILED", rebuildSw.Elapsed, e.GetType().Name + ": " + e.Message));
            AnsiConsole.MarkupLine($"[red]Rebuild FAILED after {rebuildSw.Elapsed.TotalSeconds:N1}s — skipping validate.[/]");
            AnsiConsole.WriteException(e);
            PrintSummary(summary, totalStopwatch);
            return false;
        }

        var rate = rebuildEventCount > 0 ? rebuildEventCount / Math.Max(0.001, rebuildSw.Elapsed.TotalSeconds) : 0;
        var rebuildNote = $"{rebuildEventCount:N0} events @ {rate:N0}/sec";
        if (rebuildSnapshot.Enabled)
        {
            var lw = rebuildSnapshot.ProgressionLockWaits;
            rebuildNote += $" (p50 {rebuildSnapshot.Throughput.P50:N0}, p95 {rebuildSnapshot.Throughput.P95:N0}, "
                + $"{rebuildSnapshot.NpgsqlCommandCount:N0} pg cmds, "
                + $"lock max-waiters {lw.MaxConcurrentWaiters}, waiter-sec {lw.ObservedWaiterSeconds:N1})";
        }
        summary.Add(("rebuild", "OK", rebuildSw.Elapsed, rebuildNote));

        // ---- Phase 3: validate -------------------------------------------

        if (input.SkipValidateFlag)
        {
            summary.Add(("validate", "SKIPPED", TimeSpan.Zero, "--skip-validate"));
            PrintSummary(summary, totalStopwatch);
            return true;
        }

        AnsiConsole.MarkupLine($"[blue]Phase 3: validate against [yellow]{input.BaselineFlag}[/][/]");
        var validateSw = Stopwatch.StartNew();
        try
        {
            var current = await AggregateBaselineCapture
                .CaptureAsync(ConnectionSource.ConnectionString, schemaName, CancellationToken.None)
                .ConfigureAwait(false);

            if (!File.Exists(input.BaselineFlag))
            {
                await AggregateBaselineCapture.WriteAsync(current, input.BaselineFlag).ConfigureAwait(false);
                validateSw.Stop();
                summary.Add((
                    "validate",
                    "BASELINE WRITTEN",
                    validateSw.Elapsed,
                    $"baseline did not exist; wrote {current.Tables.Count} tables to {input.BaselineFlag}"));
                PrintSummary(summary, totalStopwatch);
                return true;
            }

            var baseline = await AggregateBaselineCapture.ReadAsync(input.BaselineFlag).ConfigureAwait(false);
            var diffs = AggregateBaselineCapture.Diff(baseline, current);
            validateSw.Stop();

            if (diffs.Count == 0)
            {
                summary.Add((
                    "validate",
                    "OK",
                    validateSw.Elapsed,
                    $"{current.Tables.Count} tables match baseline"));
                PrintSummary(summary, totalStopwatch);
                return true;
            }

            summary.Add((
                "validate",
                "FAILED",
                validateSw.Elapsed,
                $"{diffs.Count} table(s) drifted"));
            foreach (var diff in diffs)
            {
                AnsiConsole.WriteLine(diff);
            }
            PrintSummary(summary, totalStopwatch);
            return false;
        }
        catch (Exception e)
        {
            validateSw.Stop();
            summary.Add(("validate", "FAILED", validateSw.Elapsed, e.GetType().Name + ": " + e.Message));
            AnsiConsole.WriteException(e);
            PrintSummary(summary, totalStopwatch);
            return false;
        }
    }

    private static InstrumentationOptions BuildInstrumentationOptions(StressInput input)
    {
        var enabled = input.InstrumentFlag
            || !string.IsNullOrWhiteSpace(input.InstrumentTraceFlag)
            || !string.IsNullOrWhiteSpace(input.InstrumentLockTraceFlag);
        return new InstrumentationOptions
        {
            Enabled = enabled,
            ProgressSampleInterval = TimeSpan.FromSeconds(Math.Max(0.1, input.InstrumentSampleSecondsFlag)),
            TracePath = string.IsNullOrWhiteSpace(input.InstrumentTraceFlag) ? null : input.InstrumentTraceFlag,
            LockTracePath = string.IsNullOrWhiteSpace(input.InstrumentLockTraceFlag) ? null : input.InstrumentLockTraceFlag
        };
    }

    private static void PrintSummary(List<(string Phase, string Status, TimeSpan Elapsed, string Note)> rows, Stopwatch total)
    {
        total.Stop();
        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("Phase")
            .AddColumn("Status")
            .AddColumn(new TableColumn("Elapsed").RightAligned())
            .AddColumn("Note");
        foreach (var row in rows)
        {
            var statusColor = row.Status switch
            {
                "OK" or "BASELINE WRITTEN" => "green",
                "SKIPPED" => "yellow",
                _ => "red"
            };
            table.AddRow(row.Phase, $"[{statusColor}]{row.Status}[/]", $"{row.Elapsed.TotalSeconds:N1}s", row.Note);
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Total elapsed: {total.Elapsed.TotalSeconds:N1}s[/]");
    }
}
