using Marten.Storage;
#nullable enable
namespace Marten.Schema
{
    public interface IDatabaseCreationExpressions
    {
        /// <param name="tenantId">If omitted, configure for default tenancy</param>
        ITenantDatabaseCreationExpressions ForTenant(string tenantId = Tenancy.DefaultTenantId);

        /// <summary>
        /// Setup the maintenance database to which to connect to prior to database creation.
        /// If not specified, the store connection string with 'postgres' as database is used.
        /// </summary>
        IDatabaseCreationExpressions MaintenanceDatabase(string maintenanceDbConnectionString);
    }
}
