using System;
using System.Linq;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Shouldly;
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
}
