
namespace Marten.Storage
{
    public class Tenant
    {
        public Tenant(string tenantId, IMartenDatabase inner)
        {
            Database = inner;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IMartenDatabase Database { get; }
    }
}
