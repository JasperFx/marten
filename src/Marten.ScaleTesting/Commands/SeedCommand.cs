using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Seeding;
using Spectre.Console;

namespace Marten.ScaleTesting.Commands;

[Description("Seed N tenants × M events each under conjoined multi-tenancy. Idempotent — rerun is a no-op if the target counts are already met.")]
public sealed class SeedCommand: JasperFxAsyncCommand<SeedInput>
{
    public override async Task<bool> Execute(SeedInput input)
    {
        using var host = input.BuildHost();
        var store = host.DocumentStore();

        if (input.WipeFlag)
        {
            AnsiConsole.MarkupLine("[red]--wipe specified — destroying all data before seeding.[/]");
            await store.Advanced.Clean.CompletelyRemoveAllAsync().ConfigureAwait(false);
        }

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        var options = input.ToSeedOptions();
        var seeder = new EventSeeder(store, options);
        var report = await seeder.RunAsync(CancellationToken.None).ConfigureAwait(false);

        if (report.AlreadySeeded)
        {
            AnsiConsole.MarkupLine("[yellow]Nothing to do — the target tenants already have enough events. Pass --wipe to start over.[/]");
            return true;
        }

        AnsiConsole.MarkupLine($"[green]Seed complete.[/]");
        var table = new Table().AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        table.AddRow("Batches written", report.Batches.ToString("N0"));
        table.AddRow("Events written", report.Events.ToString("N0"));
        table.AddRow("Elapsed", $"{report.Elapsed.TotalSeconds:N1}s");
        table.AddRow("Throughput (events/sec)", (report.Events / Math.Max(0.001, report.Elapsed.TotalSeconds)).ToString("N0"));
        AnsiConsole.Write(table);

        var stats = await store.Advanced.FetchEventStoreStatistics().ConfigureAwait(false);
        AnsiConsole.MarkupLine($"[blue]Event store now holds {stats.EventCount:N0} events across {stats.StreamCount:N0} streams.[/]");

        return true;
    }
}
