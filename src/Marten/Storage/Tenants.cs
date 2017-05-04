namespace Marten.Storage
{
    public class Tenants
    {
        public const string Default = "*DEFAULT*";
    }

    public interface ITenantStrategy
    {
        string[] AllKnownTenants();
        ITenant Create(string tenantId, AutoCreate autoCreate);
    }

    public class SingleTenant : ITenantStrategy
    {
        private readonly string _connectionString;

        public SingleTenant(string connectionString)
        {
            _connectionString = connectionString;
        }

        public string[] AllKnownTenants()
        {
            return new[] {Tenants.Default};
        }

        public ITenant Create(string tenantId, AutoCreate autoCreate)
        {
            throw new System.NotImplementedException();
        }
    }
}