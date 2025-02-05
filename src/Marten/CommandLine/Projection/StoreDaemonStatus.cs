using System.Linq;
using JasperFx.Core;
using Marten.Events.Daemon;
using Spectre.Console;

namespace Marten.CommandLine.Projection;

internal class StoreDaemonStatus
{
    public readonly LightweightCache<string, DatabaseStatus> Databases =
        new LightweightCache<string, DatabaseStatus>(name => new DatabaseStatus(name));

    public StoreDaemonStatus(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public void ReadState(string databaseName, ShardState state)
    {
        Databases[databaseName].ReadState(state);
    }

    public Table BuildTable()
    {
        var table = new Table { Title = new TableTitle(Name, Style.Parse("bold")) };
        var databases = Databases.OrderBy(x => x.Name).ToArray();
        if (databases.Length == 1)
        {
            var database = databases.Single();
            return BuildTableForSingleDatabase(table, database);
        }
        else
        {
            return BuildTableForMultipleDatabases(table, databases);
        }
    }

    private static Table BuildTableForMultipleDatabases(Table table, DatabaseStatus[] databases)
    {
        table.AddColumns("Database", "Shard", "Sequence", "Status");
        table.Columns[2].Alignment = Justify.Right;
        foreach (var database in databases)
        {
            table.AddRow(new Markup($"[blue]{database.Name}[/]"), new Markup("[blue]High Water Mark[/]"),
                new Markup($"[blue]{database.HighWaterMark}[/]"), new Markup("[gray]Active[/]"));

            foreach (var shard in database.Shards.OrderBy(x => x.ShardName))
            {
                table.AddRow(database.Name, shard.ShardName, shard.Sequence.ToString(), shard.State.ToString());
            }
        }

        return table;
    }

    private static Table BuildTableForSingleDatabase(Table table, DatabaseStatus database)
    {
        table.AddColumns("Shard", "Sequence", "Status");
        table.Columns[1].Alignment = Justify.Right;
        table.AddRow(new Markup("[blue]High Water Mark[/]"), new Markup($"[blue]{database.HighWaterMark}[/]"),
            new Markup("[gray]Active[/]"));

        foreach (var shard in database.Shards.OrderBy(x => x.ShardName))
        {
            table.AddRow(shard.ShardName, shard.Sequence.ToString(), shard.State.ToString());
        }

        return table;
    }
}
