using Marten.Storage;

namespace Marten.Schema
{
    public interface IDatabaseCreationExpressions
    {
        /// <param name="tenantId">If omitted, configure for default tenancy</param>
        /// <returns></returns>
        ITenantDatabaseCreationExpressions ForTenant(string tenantId = Tenancy.DefaultTenantId);
        /// <summary>
        /// Setup the maintenance database to which to connect to prior to database creation
        /// </summary>        
        IDatabaseCreationExpressions MaintenanceDatabase(string maintenanceDbConnectionString);        
    }
}