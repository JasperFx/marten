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

/// <summary>
/// Entities and DbContext for testing schema handling with EF Core integration.
/// Reproduces https://github.com/JasperFx/marten/issues/4175
/// </summary>
public class EntityType
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class Entity
{
    public Guid Id { get; set; }
    public int EntityTypeId { get; set; }
    public bool Featured { get; set; }
    public string InternalName { get; set; } = string.Empty;

    public EntityType EntityType { get; set; } = null!;
}

/// <summary>
/// DbContext that places entities in an explicit "test_ef_schema" schema,
/// separate from the Marten document store schema.
/// </summary>
public class SeparateSchemaDbContext : DbContext
{
    public const string EfSchema = "test_ef_schema";

    public SeparateSchemaDbContext(DbContextOptions<SeparateSchemaDbContext> options) : base(options)
    {
    }

    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<EntityType> EntityTypes => Set<EntityType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EfSchema);

        modelBuilder.Entity<EntityType>(entity =>
        {
            entity.ToTable("entity_type");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<Entity>(entity =>
        {
            entity.ToTable("entity");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.EntityType)
                .WithMany()
                .HasForeignKey(e => e.EntityTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>
/// DbContext with no explicit schema - these entities should be moved to the
/// Marten document store schema, including their FK references.
/// See https://github.com/JasperFx/marten/issues/4192
/// </summary>
public class NoEfSchemaDbContext : DbContext
{
    public NoEfSchemaDbContext(DbContextOptions<NoEfSchemaDbContext> options) : base(options)
    {
    }

    public DbSet<Entity> Entities => Set<Entity>();
    public DbSet<EntityType> EntityTypes => Set<EntityType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityType>(entity =>
        {
            entity.ToTable("entity_type");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<Entity>(entity =>
        {
            entity.ToTable("entity");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.EntityType)
                .WithMany()
                .HasForeignKey(e => e.EntityTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public class EfCoreSchemaTests
{
    [Fact]
    public void should_respect_ef_core_explicit_schema_and_not_move_tables_to_marten_schema()
    {
        // Issue #4175: When EF Core entities have an explicit schema configured,
        // AddEntityTablesFromDbContext should NOT move those tables into Marten's schema.
        const string martenSchema = "test_marten_schema";

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = martenSchema;

            opts.AddEntityTablesFromDbContext<SeparateSchemaDbContext>();
        });

        // Find the EF Core tables that were registered
        var extendedObjects = store.Options.Storage.ExtendedSchemaObjects;

        var entityTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "entity");
        var entityTypeTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "entity_type");

        entityTable.ShouldNotBeNull();
        entityTypeTable.ShouldNotBeNull();

        // These tables should remain in the EF Core schema, NOT Marten's schema
        entityTable.Identifier.Schema.ShouldBe(SeparateSchemaDbContext.EfSchema,
            "Entity table should stay in the EF Core schema, not be moved to Marten's schema");
        entityTypeTable.Identifier.Schema.ShouldBe(SeparateSchemaDbContext.EfSchema,
            "EntityType table should stay in the EF Core schema, not be moved to Marten's schema");
    }

    [Fact]
    public void should_move_tables_without_explicit_schema_to_marten_schema()
    {
        // When EF Core entities do NOT have an explicit schema,
        // they should still be moved to Marten's schema (existing behavior).
        const string martenSchema = "test_marten_schema";

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = martenSchema;

            // TestDbContext does not set an explicit schema
            opts.AddEntityTablesFromDbContext<TestDbContext>();
        });

        var extendedObjects = store.Options.Storage.ExtendedSchemaObjects;

        var orderSummariesTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "ef_order_summaries");

        orderSummariesTable.ShouldNotBeNull();
        orderSummariesTable.Identifier.Schema.ShouldBe(martenSchema,
            "Tables without explicit schema should be moved to Marten's schema");
    }

    [Fact]
    public void should_return_entity_types_in_fk_dependency_order()
    {
        // Issue #4180: GetEntityTypesForMigration should return entity types sorted
        // so that referenced tables come before referencing tables.
        var builder = new DbContextOptionsBuilder<SeparateSchemaDbContext>();
        builder.UseNpgsql("Host=localhost");

        using var dbContext = new SeparateSchemaDbContext(builder.Options);

        var entityTypes = DbContextExtensions.GetEntityTypesForMigration(dbContext);
        var names = entityTypes.Select(e => e.GetTableName()).ToList();

        // entity_type must come before entity because entity has a FK to entity_type
        var entityTypeIndex = names.IndexOf("entity_type");
        var entityIndex = names.IndexOf("entity");

        entityTypeIndex.ShouldBeGreaterThanOrEqualTo(0);
        entityIndex.ShouldBeGreaterThanOrEqualTo(0);
        entityTypeIndex.ShouldBeLessThan(entityIndex,
            "entity_type should come before entity due to FK dependency (issue #4180)");
    }

    [Fact]
    public async Task should_apply_schema_with_fk_dependencies_without_error()
    {
        // Issue #4180: End-to-end test proving that Marten can apply schema changes
        // for EF Core entities with FK dependencies without table ordering errors.
        const string testSchema = "ef_fk_order_test";

        // Clean up any previous test schema
        await using var cleanupConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await cleanupConn.OpenAsync();
        await using (var cmd = cleanupConn.CreateCommand())
        {
            cmd.CommandText = $"DROP SCHEMA IF EXISTS {testSchema} CASCADE";
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

                opts.AddEntityTablesFromDbContext<SeparateSchemaDbContext>(b =>
                {
                    b.UseNpgsql("Host=localhost");
                });
            });

            // This triggers schema creation - should not throw due to FK ordering
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            // Verify tables were created by querying the information schema
            await using var verifyConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await verifyConn.OpenAsync();
            await using var verifyCmd = verifyConn.CreateCommand();
            verifyCmd.CommandText = @"
                SELECT table_name FROM information_schema.tables
                WHERE table_schema = @schema
                ORDER BY table_name";
            verifyCmd.Parameters.AddWithValue("schema", SeparateSchemaDbContext.EfSchema);

            var tables = new System.Collections.Generic.List<string>();
            await using var reader = await verifyCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            tables.ShouldContain("entity");
            tables.ShouldContain("entity_type");
        }
        finally
        {
            // Clean up
            await using var finalConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await finalConn.OpenAsync();
            await using var finalCmd = finalConn.CreateCommand();
            finalCmd.CommandText = $"DROP SCHEMA IF EXISTS {testSchema} CASCADE";
            await finalCmd.ExecuteNonQueryAsync();
            finalCmd.CommandText = $"DROP SCHEMA IF EXISTS {SeparateSchemaDbContext.EfSchema} CASCADE";
            await finalCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void should_move_tables_and_fk_without_explicit_schema_to_marten_schema()
    {
        // Issue #4192: When EF Core entities do NOT have an explicit schema,
        // tables AND their FK references should be moved to Marten's schema.
        const string martenSchema = "test_marten_schema";

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = martenSchema;

            // NoEfSchemaDbContext does not set an explicit schema
            opts.AddEntityTablesFromDbContext<NoEfSchemaDbContext>();
        });

        var extendedObjects = store.Options.Storage.ExtendedSchemaObjects;

        var entityTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "entity");
        var entityTypeTable = extendedObjects.OfType<Table>()
            .FirstOrDefault(t => t.Identifier.Name == "entity_type");

        entityTable.ShouldNotBeNull();
        entityTypeTable.ShouldNotBeNull();

        // These tables should be moved to Marten's schema
        entityTable.Identifier.Schema.ShouldBe(martenSchema,
            "Entity table should be moved to Marten's schema");
        entityTypeTable.Identifier.Schema.ShouldBe(martenSchema,
            "EntityType table should be moved to Marten's schema");

        // FK references should also point to Marten's schema
        var entityFk = entityTable.ForeignKeys.FirstOrDefault();
        entityFk.ShouldNotBeNull("Entity should have a FK to EntityType");
        entityFk.LinkedTable!.Schema.ShouldBe(martenSchema,
            "Foreign keys should also be updated to reference the correct schema");
    }
}
