namespace Marten.Storage
{
    public class SingleTenant : ITenantStrategy
    {
        private readonly IConnectionFactory _factory;

        public SingleTenant(IConnectionFactory factory)
        {
            _factory = factory;
        }

        public string[] AllKnownTenants()
        {
            return new[] {Tenants.DefaultTenantId};
        }

        public IConnectionFactory Create(string tenantId, AutoCreate autoCreate)
        {
            return _factory;
        }
    }
}