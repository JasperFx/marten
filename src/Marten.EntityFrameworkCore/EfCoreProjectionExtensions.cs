using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Weasel.Core;
using Weasel.EntityFrameworkCore;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using ITable = Weasel.Core.ITable;

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
    public static void Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TDoc, TId,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(this StoreOptions options,
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
    public static void Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TDoc, TId,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(this StoreOptions options,
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
    public static void Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TDoc, TId,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(this CompositeProjection composite,
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
    public static void Add<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TDoc, TId,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(this CompositeProjection composite,
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
    public static void AddEntityTablesFromDbContext<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(this StoreOptions options,
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
            options.Storage.ExtendedSchemaObjects.Add(mapToMartenSchema(migrator, entityType, schemaName));
        }
    }

    /// <summary>
    /// Map one EF Core entity type onto the Weasel table Marten will migrate for it.
    /// </summary>
    /// <remarks>
    /// Only move tables to the Marten schema if the entity does NOT have an explicit schema
    /// configured in EF Core. When a user has deliberately placed entities in a separate schema
    /// (e.g., via HasDefaultSchema or ToTable("name", "schema")), that schema should be respected.
    /// See https://github.com/JasperFx/marten/issues/4175
    /// </remarks>
    private static ITable mapToMartenSchema(PostgresqlMigrator migrator, IEntityType entityType, string? schemaName)
    {
        var table = migrator.MapToTable(entityType);

        var efSchema = entityType.GetSchema();
        if (efSchema == null && !string.IsNullOrEmpty(schemaName) && table is Table pgTable)
        {
            pgTable.MoveToSchema(schemaName);
        }

        return table;
    }

    /// <summary>
    /// #5329: resolve the schema-qualified table EF Core actually persists <typeparamref name="TEntity"/>
    /// into, so a rebuild tears down the projection's real data instead of Marten's never-created
    /// <c>mt_doc_&lt;tdoc&gt;</c> table. Deliberately shares <see cref="mapToMartenSchema"/> with
    /// <see cref="AddEntityTablesFromDbContext{TDbContext}"/> — the teardown target and the migrated
    /// table have to be the same table, and they would drift apart the moment the schema rule was
    /// spelled out twice.
    /// </summary>
    /// <returns>
    /// The quoted, schema-qualified identifier, or null when the DbContext does not map the type at
    /// all — in which case the projection cannot persist through EF Core either and fails loudly on
    /// its first write, so there is nothing here to tear down.
    /// </returns>
    internal static string? ResolveEntityTableIdentifier<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
            | DynamicallyAccessedMemberTypes.NonPublicConstructors
            | DynamicallyAccessedMemberTypes.PublicFields
            | DynamicallyAccessedMemberTypes.NonPublicFields
            | DynamicallyAccessedMemberTypes.PublicProperties
            | DynamicallyAccessedMemberTypes.NonPublicProperties
            | DynamicallyAccessedMemberTypes.Interfaces)]
        TEntity,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TDbContext>(string? schemaName, Action<DbContextOptionsBuilder<TDbContext>>? configure)
        where TDbContext : DbContext
    {
        // Same throwaway DbContext trick as AddEntityTablesFromDbContext: the connection is never
        // opened, it only exists to satisfy UseNpgsql so the model can be read.
        var builder = new DbContextOptionsBuilder<TDbContext>();
        builder.UseNpgsql("Host=localhost");
        configure?.Invoke(builder);

        using var dbContext = (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options)!;

        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        if (entityType?.GetTableName() == null) return null;

        // ToString() rather than QualifiedName: EF Core tables are mapped with
        // PreserveIdentifierCase, so a PascalCase table name has to keep its quoting to be found.
        return mapToMartenSchema(new PostgresqlMigrator(), entityType, schemaName).Identifier.ToString();
    }
}
