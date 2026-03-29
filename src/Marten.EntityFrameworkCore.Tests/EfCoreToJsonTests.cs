using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Weasel.EntityFrameworkCore;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

#region Entities

/// <summary>
/// Owned type that will be stored as a JSON column via ToJson().
/// </summary>
public class ShippingAddress
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>
/// Optional owned type mapped via ToJson() to verify nullable JSON columns.
/// </summary>
public class OrderMetadata
{
    public string? Source { get; set; }
    public string? CouponCode { get; set; }
    public int LoyaltyPoints { get; set; }
}

/// <summary>
/// Entity with an owned type mapped to a JSON column via OwnsOne().ToJson().
/// </summary>
public class CustomerOrder
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public ShippingAddress ShippingAddress { get; set; } = new();
    public OrderMetadata? Metadata { get; set; }
}

#endregion

#region DbContext

/// <summary>
/// DbContext using OwnsOne().ToJson() to map owned types to JSON columns.
/// Reproduces https://github.com/JasperFx/weasel/issues/232
/// </summary>
public class ToJsonDbContext : DbContext
{
    public const string EfSchema = "ef_tojson_test";

    public ToJsonDbContext(DbContextOptions<ToJsonDbContext> options) : base(options)
    {
    }

    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EfSchema);

        modelBuilder.Entity<CustomerOrder>(entity =>
        {
            entity.ToTable("customer_orders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.Amount).HasColumnName("amount");

            // Map owned types to JSON columns - this is the scenario from GH-232
            entity.OwnsOne(e => e.ShippingAddress, b =>
            {
                b.ToJson("shipping_address");
            });

            entity.OwnsOne(e => e.Metadata, b =>
            {
                b.ToJson("metadata");
            });
        });
    }
}

#endregion

/// <summary>
/// Tests verifying that EF Core OwnsOne().ToJson() JSON column mapping is correctly
/// picked up by Weasel's schema migration pipeline via AddEntityTablesFromDbContext.
/// See https://github.com/JasperFx/weasel/issues/232
/// </summary>
public class EfCoreToJsonTests
{
    [Fact]
    public void should_map_json_columns_from_owned_entities_with_to_json()
    {
        // GH-232: OwnsOne().ToJson() owned entities should produce JSON columns
        // in the Weasel table definition.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "tojson_test";

            opts.AddEntityTablesFromDbContext<ToJsonDbContext>();
        });

        var extendedObjects = store.Options.Storage.ExtendedSchemaObjects;

        var customerOrdersTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "customer_orders");

        customerOrdersTable.ShouldNotBeNull("customer_orders table should be registered");

        // Verify the JSON columns are present
        var columns = customerOrdersTable.Columns.Select(c => c.Name).ToList();
        columns.ShouldContain("shipping_address",
            "shipping_address JSON column should be mapped from OwnsOne().ToJson()");
        columns.ShouldContain("metadata",
            "metadata JSON column should be mapped from OwnsOne().ToJson()");
    }

    [Fact]
    public void json_columns_should_be_jsonb_type()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "tojson_test";

            opts.AddEntityTablesFromDbContext<ToJsonDbContext>();
        });

        var extendedObjects = store.Options.Storage.ExtendedSchemaObjects;

        var customerOrdersTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "customer_orders");

        customerOrdersTable.ShouldNotBeNull();

        var shippingCol = customerOrdersTable.Columns.FirstOrDefault(c => c.Name == "shipping_address");
        shippingCol.ShouldNotBeNull();
        shippingCol.Type.ShouldBe("jsonb",
            "JSON columns from ToJson() should default to jsonb on PostgreSQL");

        var metadataCol = customerOrdersTable.Columns.FirstOrDefault(c => c.Name == "metadata");
        metadataCol.ShouldNotBeNull();
        metadataCol.Type.ShouldBe("jsonb");
    }

    [Fact]
    public async Task can_apply_migration_with_json_columns()
    {
        const string testSchema = "ef_tojson_migration";

        // Clean up any previous test schema
        await using var cleanupConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await cleanupConn.OpenAsync();
        await using (var cmd = cleanupConn.CreateCommand())
        {
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {testSchema} CASCADE";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {ToJsonDbContext.EfSchema} CASCADE";
            await cmd.ExecuteNonQueryAsync();
        }

        await cleanupConn.CloseAsync();

        try
        {
            await using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = testSchema;
                opts.AutoCreateSchemaObjects = AutoCreate.All;

                opts.AddEntityTablesFromDbContext<ToJsonDbContext>(b =>
                {
                    b.UseNpgsql("Host=localhost");
                });
            });

            // This triggers schema creation - should succeed with JSON columns
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            // Verify the table was created with JSON columns
            await using var verifyConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await verifyConn.OpenAsync();
            await using var verifyCmd = verifyConn.CreateCommand();
            verifyCmd.CommandText = @"
                SELECT column_name, data_type
                FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = 'customer_orders'
                ORDER BY ordinal_position";
            verifyCmd.Parameters.AddWithValue("schema", ToJsonDbContext.EfSchema);

            var columnMap = new System.Collections.Generic.Dictionary<string, string>();
            await using var reader = await verifyCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columnMap[reader.GetString(0)] = reader.GetString(1);
            }

            columnMap.ShouldContainKey("shipping_address");
            columnMap["shipping_address"].ShouldBe("jsonb",
                "shipping_address column should be jsonb in the actual database");

            columnMap.ShouldContainKey("metadata");
            columnMap["metadata"].ShouldBe("jsonb",
                "metadata column should be jsonb in the actual database");
        }
        finally
        {
            // Clean up
            await using var finalConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await finalConn.OpenAsync();
            await using var finalCmd = finalConn.CreateCommand();
            finalCmd.CommandText = $"DROP SCHEMA IF EXISTS {testSchema} CASCADE";
            await finalCmd.ExecuteNonQueryAsync();
            finalCmd.CommandText = $"DROP SCHEMA IF EXISTS {ToJsonDbContext.EfSchema} CASCADE";
            await finalCmd.ExecuteNonQueryAsync();
        }
    }
}
