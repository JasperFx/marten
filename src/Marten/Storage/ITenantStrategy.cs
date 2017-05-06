namespace Marten.Storage
{
    public interface ITenantStrategy
    {
        string[] AllKnownTenants();
        IConnectionFactory Create(string tenantId, AutoCreate autoCreate);
    }
}