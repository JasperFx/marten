using System.Diagnostics;
using JasperFx;
using JasperFx.CommandLine;
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

        // Ensure the schema is in place so a brand-new database can still
        // rebuild even if the user skipped the `seed` step (no events =
        // immediate no-op).
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var stats = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
        AnsiConsole.MarkupLine(
            $"[blue]Rebuilding projection [yellow]{input.ProjectionFlag}[/] against {stats.EventCount:N0} events / {stats.StreamCount:N0} streams.[/]");

        using var daemon = await store.BuildProjectionDaemonAsync().ConfigureAwait(false);

        var shardTimeout = TimeSpan.FromSeconds(input.ShardTimeoutSecondsFlag);
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
        AnsiConsole.Write(table);

        return true;
    }
}
