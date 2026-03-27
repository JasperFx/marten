using System;
using System.Collections.Generic;
using JasperFx;
using Npgsql;
using Weasel.Core.MultiTenancy;

namespace Marten.Storage;

/// <summary>
/// Configuration options for sharded multi-tenancy where tenants are distributed
/// across multiple databases with native PG list partitioning per tenant within each database.
/// </summary>
public class ShardedTenancyOptions
{
    /// <summary>
    /// Connection string to the master database that holds the pool registry
    /// and tenant assignment tables.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Pre-configured NpgsqlDataSource for the master database.
    /// Takes precedence over ConnectionString if set.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Schema name for the pool registry and assignment tables in the master database.
    /// Defaults to "public".
    /// </summary>
    public string SchemaName { get; set; } = "public";

    /// <summary>
    /// Schema name for the mt_tenant_partitions table within each tenant database.
    /// Defaults to "tenants".
    /// </summary>
    public string PartitionSchemaName { get; set; } = "tenants";

    /// <summary>
    /// Override the AutoCreate setting for the master database tables.
    /// If null, uses the store's AutoCreateSchemaObjects setting.
    /// </summary>
    public AutoCreate? AutoCreate { get; set; }

    /// <summary>
    /// Application name tag for diagnostics in connection strings.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// The strategy used to assign new tenants to databases.
    /// Defaults to <see cref="HashTenantAssignment"/>.
    /// </summary>
    public ITenantAssignmentStrategy AssignmentStrategy { get; set; } = new HashTenantAssignment();

    private readonly List<(string DatabaseId, string ConnectionString)> _seedDatabases = new();

    /// <summary>
    /// Seed databases that are registered in the pool on startup.
    /// </summary>
    public IReadOnlyList<(string DatabaseId, string ConnectionString)> SeedDatabases => _seedDatabases;

    /// <summary>
    /// Register a database in the pool at startup.
    /// </summary>
    public void AddDatabase(string databaseId, string connectionString)
    {
        _seedDatabases.Add((databaseId, connectionString));
    }

    /// <summary>
    /// Use hash-based tenant assignment (deterministic, FNV-1a hash % N).
    /// This is the default.
    /// </summary>
    public void UseHashAssignment()
    {
        AssignmentStrategy = new HashTenantAssignment();
    }

    /// <summary>
    /// Use smallest-database tenant assignment (picks database with lowest tenant count).
    /// </summary>
    public void UseSmallestDatabaseAssignment(IDatabaseSizingStrategy? sizing = null)
    {
        AssignmentStrategy = new SmallestTenantAssignment(sizing);
    }

    /// <summary>
    /// Use explicit-only tenant assignment. Unknown tenants throw UnknownTenantIdException.
    /// All tenants must be pre-assigned via the admin API.
    /// </summary>
    public void UseExplicitAssignment()
    {
        AssignmentStrategy = new ExplicitTenantAssignment();
    }

    /// <summary>
    /// Use a custom tenant assignment strategy.
    /// </summary>
    public void UseCustomAssignment(ITenantAssignmentStrategy strategy)
    {
        AssignmentStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
    }

    internal string CorrectedConnectionString(string connectionString)
    {
        if (ApplicationName == null) return connectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName
        };
        return builder.ConnectionString;
    }
}
