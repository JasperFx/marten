namespace Marten
{
    /// <summary>
    /// Access to querying or registering updates for a separate tenant
    /// to a parent IDocumentSession
    /// </summary>
    public interface ITenantOperations: IDocumentOperations
    {
        /// <summary>
        /// The tenant id of this tenant operations
        /// </summary>
        string TenantId { get; }

        IDocumentSession Parent { get; }
    }
}
