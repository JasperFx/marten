using System;
using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Microsoft.EntityFrameworkCore;
using Weasel.Core;
using Weasel.EntityFrameworkCore;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.EntityFrameworkCore;

/// <summary>
/// Extension methods for registering EF Core projections with Marten.
/// </summary>
public static class EfCoreProjectionExtensions
{
    /// <summary>
    /// Register an <see cref="EfCoreSingleStreamProjection{TDoc,TId,TDbContext}"/> with Marten.
    /// Automatically sets up EF Core-based aggregate persistence and Weasel schema migration
    /// for all entity types in the DbContext.
    /// </summary>
    public static void Add<TDoc, TId, TDbContext>(this StoreOptions options,
        EfCoreSingleStreamProjection<TDoc, TId, TDbContext> projection,
        ProjectionLifecycle lifecycle)
        where TDoc : class where TId : notnull where TDbContext : DbContext
    {
        projection.RegisterEfCoreStorage(options);
        options.Projections.Add(projection, lifecycle);
        options.AddEntityTablesFromDbContext<TDbContext>(projection.ConfigureDbContext);
    }

    /// <summary>
    /// Register an <see cref="EfCoreMultiStreamProjection{TDoc,TId,TDbContext}"/> with Marten.
    /// Automatically sets up EF Core-based aggregate persistence and Weasel schema migration
    /// for all entity types in the DbContext.
    /// </summary>
    public static void Add<TDoc, TId, TDbContext>(this StoreOptions options,
        EfCoreMultiStreamProjection<TDoc, TId, TDbContext> projection,
        ProjectionLifecycle lifecycle)
        where TDoc : class where TId : notnull where TDbContext : DbContext
    {
        projection.RegisterEfCoreStorage(options);
        options.Projections.Add(projection, lifecycle);
        options.AddEntityTablesFromDbContext<TDbContext>(projection.ConfigureDbContext);
    }

    /// <summary>
    /// Add an <see cref="EfCoreMultiStreamProjection{TDoc,TId,TDbContext}"/> to a composite projection.
    /// Registers EF Core-based aggregate persistence and Weasel schema migration.
    /// </summary>
    public static void Add<TDoc, TId, TDbContext>(this CompositeProjection composite,
        StoreOptions options,
        EfCoreMultiStreamProjection<TDoc, TId, TDbContext> projection,
        int stageNumber = 1)
        where TDoc : class where TId : notnull where TDbContext : DbContext
    {
        projection.RegisterEfCoreStorage(options);
        composite.Add(projection, stageNumber);
        options.AddEntityTablesFromDbContext<TDbContext>(projection.ConfigureDbContext);
    }

    /// <summary>
    /// Add an <see cref="EfCoreSingleStreamProjection{TDoc,TId,TDbContext}"/> to a composite projection.
    /// Registers EF Core-based aggregate persistence and Weasel schema migration.
    /// </summary>
    public static void Add<TDoc, TId, TDbContext>(this CompositeProjection composite,
        StoreOptions options,
        EfCoreSingleStreamProjection<TDoc, TId, TDbContext> projection,
        int stageNumber = 1)
        where TDoc : class where TId : notnull where TDbContext : DbContext
    {
        projection.RegisterEfCoreStorage(options);
        composite.Add(projection, stageNumber);
        options.AddEntityTablesFromDbContext<TDbContext>(projection.ConfigureDbContext);
    }

    /// <summary>
    /// Register EF Core entity tables from a <typeparamref name="TDbContext"/> with Marten's
    /// Weasel migration pipeline. Tables defined in the DbContext's model will be created
    /// and migrated automatically alongside Marten's own schema objects.
    /// </summary>
    public static void AddEntityTablesFromDbContext<TDbContext>(this StoreOptions options,
        Action<DbContextOptionsBuilder<TDbContext>>? configure = null)
        where TDbContext : DbContext
    {
        var migrator = new PostgresqlMigrator();

        // Create a temporary DbContext just to read its entity model.
        // The connection is never opened; it's only needed to satisfy UseNpgsql's requirement.
        var builder = new DbContextOptionsBuilder<TDbContext>();
        builder.UseNpgsql("Host=localhost");
        configure?.Invoke(builder);

        using var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options)!;

        var schemaName = options.DatabaseSchemaName;

        foreach (var entityType in DbContextExtensions.GetEntityTypesForMigration(dbContext))
        {
            var table = migrator.MapToTable(entityType);

            // Only move tables to the Marten schema if the entity does NOT have an
            // explicit schema configured in EF Core. When a user has deliberately placed
            // entities in a separate schema (e.g., via HasDefaultSchema or ToTable("name", "schema")),
            // that schema should be respected. See https://github.com/JasperFx/marten/issues/4175
            var efSchema = entityType.GetSchema();
            if (efSchema == null && !string.IsNullOrEmpty(schemaName) && table is Table pgTable)
            {
                pgTable.MoveToSchema(schemaName);
            }

            options.Storage.ExtendedSchemaObjects.Add(table);
        }
    }
}
