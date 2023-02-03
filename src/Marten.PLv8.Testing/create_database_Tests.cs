using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Sdk;

namespace Marten.PLv8.Testing;

[Collection("multi-tenancy")]
public class create_database_Tests : IDisposable
{
    [Fact]
    public async Task can_create_new_database_when_one_does_not_exist_with_plv8_extension()
    {
        using var store = DocumentStore.For(_ =>
        {
            _.Connection(dbToCreateConnectionString);
            _.UseJavascriptTransformsAndPatching();
            _.CreateDatabasesForTenants(c =>
            {
                c.MaintenanceDatabase(ConnectionSource.ConnectionString);
                c.ForTenant()
                    .CheckAgainstPgDatabase()
                    .WithOwner("postgres");
            });
        });

        // That should be done with Hosted Service, but let's test it also here
        var databaseGenerator = new DatabaseGenerator();
        await databaseGenerator.CreateDatabasesAsync(store.Tenancy, store.Options.CreateDatabases).ConfigureAwait(false);
        await store.Tenancy.Default.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var connection = store.Tenancy.Default.Database.CreateConnection();
        await using var command = connection.CreateCommand();

        connection.Open();
        command.CommandText = "SELECT extname FROM pg_extension";
        var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (reader["extname"].ToString().ToLowerInvariant() == "plv8")
            {
                return;
            }
        }
        connection.Close();

        throw new XunitException("Expected plv8 extension created");
    }

    private readonly string dbToCreateConnectionString;
    private readonly string dbName;

    private static Tuple<string, string> DbToCreate(string cstring)
    {
        var builder = new NpgsqlConnectionStringBuilder(cstring);
        builder.Database = $"_dropme{DateTime.UtcNow.Ticks}_{builder.Database}";
        return Tuple.Create(builder.ToString(), builder.Database);
    }

    public create_database_Tests()
    {
        var db = DbToCreate(ConnectionSource.ConnectionString);
        dbToCreateConnectionString = db.Item1;
        dbName = db.Item2;
    }

    private static bool TryDropDb(string db)
    {
        try
        {
            using (var connection = new NpgsqlConnection(ConnectionSource.ConnectionString))
            using (var cmd = connection.CreateCommand())
            {
                try
                {
                    connection.Open();
                    // Ensure connections to DB are killed - there seems to be a lingering idle session after AssertDatabaseMatchesConfiguration(), even after store disposal
                    cmd.CommandText +=
                        $"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '{db}' AND pid <> pg_backend_pid();";
                    cmd.CommandText += $"DROP DATABASE IF EXISTS {db};";
                    cmd.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                    connection.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        TryDropDb(dbName);
    }
}
