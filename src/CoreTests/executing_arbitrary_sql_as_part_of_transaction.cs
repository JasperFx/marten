using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests;

public class executing_arbitrary_sql_as_part_of_transaction : OneOffConfigurationsContext
{
    [Fact]
    public async Task can_run_extra_sql()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.RunSqlAsync("drop table if exists names cascade");
        }

        StoreOptions(opts =>
        {
            var table = new Table("names");
            table.AddColumn<string>("name").AsPrimaryKey();

            opts.Storage.ExtendedSchemaObjects.Add(table);

            table = new Table("data");
            table.AddColumn("raw_value", "jsonb");

            opts.Storage.ExtendedSchemaObjects.Add(table);
        });

        await TheStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        #region sample_QueueSqlCommand
        TheSession.QueueSqlCommand("insert into names (name) values ('Jeremy')");
        TheSession.QueueSqlCommand("insert into names (name) values ('Babu')");
        TheSession.Store(Target.Random());
        TheSession.QueueSqlCommand("insert into names (name) values ('Oskar')");
        TheSession.Store(Target.Random());
        var json = "{ \"answer\": 42 }";
        TheSession.QueueSqlCommand("insert into data (raw_value) values (?::jsonb)", json);
        #endregion

        await TheSession.SaveChangesAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();

            var names = await conn.CreateCommand("select name from names order by name")
                .FetchListAsync<string>();

            names.ShouldHaveTheSameElementsAs("Babu", "Jeremy", "Oskar");
        }
    }
}
