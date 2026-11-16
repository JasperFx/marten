namespace Marten.Exceptions;

/// <summary>
/// Thrown by sharded multi-tenancy when a tenant is already assigned to a shard database that is no longer
/// registered in the database pool. The tenant is deliberately NOT reassigned to another shard, because its
/// existing data lives on the assigned one: restore the shard's pool registration (for example with
/// <c>AddDatabase()</c> or <c>ITenantDatabasePool.AddDatabaseAsync()</c>), or move the tenant explicitly.
/// </summary>
public class AssignedShardNotFoundException: MartenException
{
    public AssignedShardNotFoundException(string tenantId, string databaseId)
        : base($"Tenant '{tenantId}' is assigned to shard database '{databaseId}', but that database is not registered in the sharded tenancy's database pool. The tenant was not reassigned, because its data is on '{databaseId}'. Register the database in the pool again, or assign the tenant explicitly.")
    {
        TenantId = tenantId;
        DatabaseId = databaseId;
    }

    /// <summary>
    /// The tenant being resolved
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    /// The shard database the tenant is assigned to, which the pool does not contain
    /// </summary>
    public string DatabaseId { get; }
}
