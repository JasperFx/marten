using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Spectre.Console;

namespace Marten.CommandLine.Projection;

internal class ConsoleView: IConsoleView
{
    public void DisplayNoStoresMessage()
    {
        AnsiConsole.Markup("[gray]No document stores in this application.[/]");
    }

    public void ListShards(IProjectionStore store)
    {
        var projections = store.Shards.Select(x => x.Source).Distinct().ToArray();

        if (projections.IsEmpty())
        {
            AnsiConsole.Markup("[gray]No projections in this store.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table();
        table.AddColumn("Projection Name");
        table.AddColumn("Class");
        table.AddColumn("Shards");
        table.AddColumn("Lifecycle");

        foreach (var projection in projections)
        {
            var shards = store.Shards.Where(x => x.Source == projection).Select(x => x.Name.Identity).Join(", ");
            table.AddRow(projection.ProjectionName, projection.GetType().FullNameInCode(), shards,
                projection.Lifecycle.ToString());
        }

        AnsiConsole.Render(table);
        AnsiConsole.WriteLine();
    }

    public void DisplayEmptyEventsMessage(IProjectionStore store)
    {
        AnsiConsole.MarkupLine("[bold]The event storage is empty, aborting.[/]");
    }

    public void DisplayRebuildIsComplete()
    {
        AnsiConsole.Markup("[green]Projection Rebuild complete![/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public void DisplayInvalidShardTimeoutValue()
    {
        AnsiConsole.Markup("[red]Invalid Shard Timeout.[/]");
    }

    public string[] SelectStores(string[] storeNames)
    {
        return AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title("Choose document stores")
            .AddChoices(storeNames)).ToArray();
    }

    public string[] SelectProjections(string[] projectionNames)
    {
        return AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title("Choose projections")
            .AddChoices(projectionNames)).ToArray();
    }

    public void DisplayNoMatchingProjections()
    {
        AnsiConsole.Markup("[gray]No projections match the criteria.[/]");
        AnsiConsole.WriteLine();
    }

    public void WriteHeader(IProjectionStore store)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[bold blue]{store.Name}[/]") { Justification = Justify.Left });
        AnsiConsole.WriteLine();
    }

    public void DisplayNoDatabases()
    {
        AnsiConsole.Markup("[gray]No named databases match the criteria.[/]");
        AnsiConsole.WriteLine();
    }

    public void DisplayNoAsyncProjections()
    {
        AnsiConsole.Markup("[gray]No asynchronous projections match the criteria.[/]");
        AnsiConsole.WriteLine();
    }

    public void WriteHeader(IProjectionDatabase database)
    {
        AnsiConsole.Write(new Rule($"Database: {database.Identifier}") { Justification = Justify.Left });
    }

    public string[] SelectDatabases(string[] databaseNames)
    {
        return AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title("Choose databases")
            .AddChoices(databaseNames)).ToArray();
    }
}
