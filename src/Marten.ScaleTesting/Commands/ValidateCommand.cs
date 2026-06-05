using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Validation;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

[Description("Capture an aggregate snapshot (row counts + content hashes per projection table) and diff against a baseline JSON. " +
             "First run with no baseline writes the file and exits 0. Subsequent runs diff; non-empty diff exits 1. " +
             "Pair with `seed` + `rebuild` (or run via `stress`) to gate rebuild determinism end-to-end.")]
public sealed class ValidateCommand: JasperFxAsyncCommand<ValidateInput>
{
    public override async Task<bool> Execute(ValidateInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore();
        var schemaName = store.Options.DatabaseSchemaName ?? "public";

        AnsiConsole.MarkupLine($"[blue]Capturing aggregate snapshot for schema [yellow]{schemaName}[/]…[/]");
        var current = await AggregateBaselineCapture
            .CaptureAsync(ConnectionSource.ConnectionString, schemaName, CancellationToken.None)
            .ConfigureAwait(false);

        // One-row table summarising what we captured.
        var summary = new Table().AddColumn("Table").AddColumn(new TableColumn("Rows").RightAligned()).AddColumn("Hash (head)");
        foreach (var snap in current.Tables.OrderBy(x => x.Table, StringComparer.Ordinal))
        {
            summary.AddRow(snap.Table, snap.RowCount.ToString("N0"), snap.DataHash[..16] + "…");
        }
        AnsiConsole.Write(summary);

        var baselineExists = File.Exists(input.BaselineFlag);

        if (input.WriteBaselineFlag || !baselineExists)
        {
            await AggregateBaselineCapture.WriteAsync(current, input.BaselineFlag).ConfigureAwait(false);
            AnsiConsole.MarkupLine(baselineExists
                ? $"[yellow]Baseline overwritten at {input.BaselineFlag} (—write-baseline supplied).[/]"
                : $"[yellow]Baseline did not exist; wrote initial baseline to {input.BaselineFlag}. Re-run validate after a fresh rebuild to diff.[/]");
            return true;
        }

        var baseline = await AggregateBaselineCapture.ReadAsync(input.BaselineFlag).ConfigureAwait(false);
        var diffs = AggregateBaselineCapture.Diff(baseline, current);
        if (diffs.Count == 0)
        {
            AnsiConsole.MarkupLine($"[green]Validate PASS — current matches baseline {input.BaselineFlag} ({baseline.Tables.Count} tables).[/]");
            return true;
        }

        AnsiConsole.MarkupLine($"[red]Validate FAIL — {diffs.Count} table(s) drifted from baseline {input.BaselineFlag}:[/]");
        foreach (var diff in diffs)
        {
            AnsiConsole.WriteLine(diff);
        }
        return false;
    }
}
