using System;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Storage.Encryption;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;

namespace MultiTenancyTests;


[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class using_master_table_multi_tenancy_with_aes_encryption: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore theStore;
    private string tenant1ConnectionString;

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);

        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync("tenants");

        tenant1ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant1");

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);
                services.AddMarten(opts =>
                    {
                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();
                    })
                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();

                services.ConfigureMarten((sp, so) =>
                {
                    so.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                    {
                        x.DataSource = sp.GetRequiredService<NpgsqlDataSource>();
                        x.ConnectionStringEncryptionOpts.UseAes(KeyGenerator.GenerateKey());
                        x.SchemaName = "tenants";
                        x.ApplicationName = "Sample";
                        x.RegisterDatabase("tenant1", tenant1ConnectionString);
                    });
                });
            }).StartAsync();


        theStore = _host.Services.GetRequiredService<IDocumentStore>();

        await _host.ClearAllTenantDatabaseRecordsAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }

    [Theory]
    [InlineData(8)]   // Too short
    [InlineData(15)]  // Just under minimum
    [InlineData(33)]  // Just over maximum
    [InlineData(64)]  // Way too long
    public void aes_encryption_rejects_invalid_key_lengths(int keyBytes)
    {
        // Arrange
        var key = KeyGenerator.GenerateKey(keyBytes, true);

        // Act & Assert
        Should.Throw<ArgumentException>(() =>
        {
            var opts = new EncryptionOptions().UseAes(key);
        }).Message.ShouldContain("AES encryption key must be between 16 and 32 bytes (128-256 bits)");
    }

    [Fact]
    public async Task can_open_a_session_to_a_different_database()
    {
        await _host.AddTenantDatabaseAsync("tenant1", tenant1ConnectionString);

        await using var session =
            theStore.LightweightSession(new SessionOptions { TenantId = "tenant1" });
    }
}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class using_master_table_multi_tenancy_with_pgcrypto_encryption: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore theStore;
    private string tenant1ConnectionString;

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync("tenants");

        tenant1ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant1");

        await conn.CloseAsync();

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNpgsqlDataSource(ConnectionSource.ConnectionString);
                services.AddMarten(opts =>
                    {
                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                        {
                            x.AutoCreate = AutoCreate.CreateOrUpdate;
                            x.ConnectionString = ConnectionSource.ConnectionString;
                            x.ConnectionStringEncryptionOpts.UsePgCrypto(KeyGenerator.GenerateKey());
                            x.SchemaName = "tenants";
                            x.ApplicationName = "Sample";
                            x.RegisterDatabase("tenant1", tenant1ConnectionString);
                        });
                    })
                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        theStore = _host.Services.GetRequiredService<IDocumentStore>();

        await _host.ClearAllTenantDatabaseRecordsAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }

    [Theory]
    [InlineData(8)]   // Too short
    [InlineData(15)]  // Just under minimum
    public void pgcrypto_encryption_rejects_invalid_key_lengths(int keyBytes)
    {
        // Arrange
        var key = KeyGenerator.GenerateKey(keyBytes, true);

        // Act & Assert
        Should.Throw<ArgumentException>(() =>
        {
            var opts = new EncryptionOptions().UsePgCrypto(key);
        }).Message.ShouldContain("at least 16 bytes");
    }

    [Fact]
    public async Task can_open_a_session_to_a_different_database()
    {
        await _host.AddTenantDatabaseAsync("tenant1", tenant1ConnectionString);

        await using var session =
            theStore.LightweightSession(new SessionOptions { TenantId = "tenant1" });

        session.Connection.Database.ShouldBe("tenant1");
    }
}


