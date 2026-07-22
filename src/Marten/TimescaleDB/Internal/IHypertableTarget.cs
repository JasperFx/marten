using System;
using System.Linq;
using Marten.Events;
using Marten.Events.Projections.Flattened;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.TimescaleDB.Internal;

/// <summary>
/// Describes one configured hypertable and knows how to resolve the underlying
/// Marten-managed table name from the finalized <see cref="StoreOptions"/> at schema-application time.
/// </summary>
internal interface IHypertableTarget
{
    HypertableOptions Options { get; }

    /// <summary>
    /// Resolve the fully-qualified table name, or null if the target table cannot be found
    /// (e.g. a projection type that was never registered).
    /// </summary>
    DbObjectName? ResolveTable(StoreOptions options);

    /// <summary>
    /// Throw a descriptive exception if the resolved table cannot legally become a hypertable
    /// on the configured time column (TimescaleDB requires the time column to participate in
    /// every unique/primary key).
    /// </summary>
    void AssertValid(StoreOptions options);
}

/// <summary>
/// A hypertable backed by a registered <see cref="Marten.Events.Projections.Flattened.FlatTableProjection"/>.
/// </summary>
internal sealed class ProjectionHypertableTarget: IHypertableTarget
{
    private readonly Type _projectionType;

    public ProjectionHypertableTarget(Type projectionType, HypertableOptions options)
    {
        _projectionType = projectionType;
        Options = options;
    }

    public HypertableOptions Options { get; }

    public DbObjectName? ResolveTable(StoreOptions options)
    {
        var projection = options.Projections.All
            .FirstOrDefault(x => _projectionType.IsInstanceOfType(x));

        if (projection is FlatTableProjection flat)
        {
            return ResolveSchema(flat, options);
        }

        return null;
    }

    public void AssertValid(StoreOptions options)
    {
        var projection = options.Projections.All
            .FirstOrDefault(x => _projectionType.IsInstanceOfType(x));

        if (projection is not FlatTableProjection flat)
        {
            return;
        }

        // TimescaleDB requires the partition column to be part of every unique/primary key.
        // FlatTableProjection always has exactly one primary-key column and upserts ON CONFLICT
        // against it, so the only shape that maps cleanly onto a hypertable is one where that
        // single primary-key column IS the configured time column (a time-bucketed rollup).
        var pkColumns = flat.Table.PrimaryKeyColumns;
        var timeColumnIsPrimaryKey = pkColumns.Count == 1
            && string.Equals(pkColumns[0], Options.TimeColumn, StringComparison.OrdinalIgnoreCase);

        if (!timeColumnIsPrimaryKey)
        {
            throw new InvalidOperationException(
                $"Cannot turn the flat-table projection '{_projectionType.Name}' into a TimescaleDB hypertable on column '{Options.TimeColumn}'. " +
                $"TimescaleDB requires the partition column to participate in every unique/primary key, but this projection's primary key is " +
                $"[{string.Join(", ", pkColumns)}]. Make '{Options.TimeColumn}' the projection's single primary-key column " +
                $"(e.g. Table.AddColumn<DateTimeOffset>(\"{Options.TimeColumn}\").AsPrimaryKey() plus a matching tablePrimaryKeySource) so the " +
                "upsert's ON CONFLICT target and the hypertable partition column agree.");
        }
    }

    // The flat table's schema is only finalized (via Table.MoveToSchema) while the eventstore
    // feature builds its objects. Replicate the SchemaNameSource switch so resolution does not
    // depend on feature enumeration order.
    private static DbObjectName ResolveSchema(FlatTableProjection flat, StoreOptions options)
    {
        var table = flat.Table.Identifier;
        var schema = flat.SchemaNameSource switch
        {
            SchemaNameSource.DocumentSchema => options.DatabaseSchemaName,
            SchemaNameSource.EventSchema => ((EventGraph)options.Events).DatabaseSchemaName,
            _ => table.Schema
        };

        return new PostgresqlObjectName(schema, table.Name);
    }
}

/// <summary>
/// A hypertable backed by a Marten document table. The configured time column is duplicated into
/// the document table's primary key (done by <see cref="TimescaleDBOptions.DocumentAsHypertable{T}"/>)
/// so Marten's schema model already matches the hypertable shape.
/// </summary>
internal sealed class DocumentHypertableTarget: IHypertableTarget
{
    private readonly Type _documentType;

    public DocumentHypertableTarget(Type documentType, HypertableOptions options)
    {
        _documentType = documentType;
        Options = options;
    }

    public HypertableOptions Options { get; }

    public DbObjectName? ResolveTable(StoreOptions options)
    {
        return options.Storage.MappingFor(_documentType).TableName;
    }

    public void AssertValid(StoreOptions options)
    {
        var mapping = options.Storage.MappingFor(_documentType);
        var pkContainsTimeColumn = mapping.Schema.Table.PrimaryKeyColumns
            .Any(c => string.Equals(c, Options.TimeColumn, StringComparison.OrdinalIgnoreCase));

        if (!pkContainsTimeColumn)
        {
            throw new InvalidOperationException(
                $"Cannot turn the document table for '{_documentType.Name}' into a TimescaleDB hypertable on column " +
                $"'{Options.TimeColumn}' because that column is not part of the table's primary key.");
        }
    }
}
