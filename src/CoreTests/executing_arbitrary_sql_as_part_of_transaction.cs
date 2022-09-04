using System.Threading.Tasks;
using Baseline;
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
        using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.RunSql("drop table if exists names cascade");
        }

        StoreOptions(opts =>
        {
            var table = new Table("names");
            table.AddColumn<string>("name").AsPrimaryKey();

            opts.Storage.ExtendedSchemaObjects.Add(table);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        #region sample_QueueSqlCommand
        theSession.QueueSqlCommand("insert into names (name) values ('Jeremy')");
        theSession.QueueSqlCommand("insert into names (name) values ('Babu')");
        theSession.Store(Target.Random());
        theSession.QueueSqlCommand("insert into names (name) values ('Oskar')");
        theSession.Store(Target.Random());
        #endregion

        await theSession.SaveChangesAsync();

        using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();

            var names = await conn.CreateCommand("select name from names order by name")
                .FetchList<string>();

            names.ShouldHaveTheSameElementsAs("Babu", "Jeremy", "Oskar");
        }
    }
}