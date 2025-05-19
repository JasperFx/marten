﻿using System;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace StressTests;

[Collection("multi-tenancy")]
public class create_database_Tests : IDisposable
{
    public static async Task example()
    {
        #region sample_marten_create_database
        var maintenanceConnectionString = ConnectionSource.ConnectionString;
        var applicationConnectionString = "";
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(options =>
        {
            // This might be different than the maintenance connection string
            options.Connection(applicationConnectionString);

            options.CreateDatabasesForTenants(c =>
            {
                // Specify a db to which to connect in case database needs to be created.
                // If not specified, defaults to 'postgres' on the connection for a tenant.
                c.MaintenanceDatabase(maintenanceConnectionString);
                c.ForTenant()
                    .CheckAgainstPgDatabase()

                    .WithOwner("postgres")
                    .WithEncoding("UTF-8")
                    .ConnectionLimit(-1);
            });
        });

        using var host = builder.Build();

        // NOTE: The new database will only be built upon the call to IHost.StartAsync()
        await host.StartAsync();
        #endregion
    }

    [Fact]
    public async Task can_create_new_database_when_one_does_not_exist_for_default_tenant_with_DatabaseGenerator()
    {
        var cstring = ConnectionSource.ConnectionString;

        TryDropDb(dbName);

        using (var store1 = DocumentStore.For(_ =>
               {
                   _.Connection(dbToCreateConnectionString);
               }))
        {
            await Should.ThrowAsync<PostgresException>(async () =>
            {
                await store1.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            });
        }

        var dbCreated = false;

        using var store = DocumentStore.For(storeOptions =>
        {
            storeOptions.Connection(dbToCreateConnectionString);

            storeOptions.CreateDatabasesForTenants(c =>
            {
                // Specify a db to which to connect in case database needs to be created.
                // If not specified, defaults to 'postgres' on the connection for a tenant.
                c.MaintenanceDatabase(cstring);

                c.ForTenant()
                    .CheckAgainstPgDatabase()
                    .DropExisting()
                    .WithOwner("postgres")
                    .WithEncoding("UTF-8")
                    .ConnectionLimit(-1)
                    .OnDatabaseCreated(_ =>
                    {
                        dbCreated = true;
                    });
            });

        });
        // That should be done with Hosted Service, but let's test it also here
        var databaseGenerator = new DatabaseGenerator();
        await databaseGenerator.CreateDatabasesAsync(store.Tenancy, store.Options.CreateDatabases).ConfigureAwait(false);

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
        Assert.True(dbCreated);
    }

    [Fact]
    public async Task can_use_existing_database_without_calling_into_create()
    {
        var user1 = new User { FirstName = "User" };
        var dbCreated = false;
        using var store = DocumentStore.For(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Connection(ConnectionSource.ConnectionString);
            opts.CreateDatabasesForTenants(c =>
            {
                c.MaintenanceDatabase(ConnectionSource.ConnectionString);
                c.ForTenant()
                    .CheckAgainstPgDatabase()
                    .WithOwner("postgres")
                    .WithEncoding("UTF-8")
                    .ConnectionLimit(-1)
                    .OnDatabaseCreated(___ => dbCreated = true);
            });

        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var session = store.LightweightSession();
        session.Store(user1);
        await session.SaveChangesAsync();

        Assert.False(dbCreated);
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
