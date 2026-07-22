#nullable enable
using JasperFx;
using Npgsql;

namespace Marten.Schema;

public interface IDatabaseCreationExpressions
{
    /// <param name="tenantId">If omitted, configure for default tenancy</param>
    ITenantDatabaseCreationExpressions ForTenant(string tenantId = StorageConstants.DefaultTenantId);

    /// <summary>
    ///     Setup the maintenance database to which to connect to prior to database creation.
    ///     If not specified, the store connection string with 'postgres' as database is used.
    /// </summary>
    IDatabaseCreationExpressions MaintenanceDatabase(string maintenanceDbConnectionString);

    /// <summary>
    ///     Supply a caller-owned <see cref="NpgsqlDataSource" /> for the maintenance connection so that
    ///     provisioning can authenticate with rotating credentials — for example an Azure Entra ID /
    ///     managed-identity access token supplied through Npgsql's periodic password provider. Point the
    ///     data source at an administrative database (typically <c>postgres</c>) whose principal holds the
    ///     <c>CREATEDB</c> privilege. The data source is never disposed by Marten (matching
    ///     <c>StoreOptions.Connection(NpgsqlDataSource)</c>); only the connections rented from it are.
    /// </summary>
    /// <remarks>
    ///     Takes precedence over any connection string passed to <see cref="MaintenanceDatabase(string)" />;
    ///     the two overloads are last-writer-wins.
    /// </remarks>
    IDatabaseCreationExpressions MaintenanceDatabase(NpgsqlDataSource maintenanceDataSource) => this;
}
