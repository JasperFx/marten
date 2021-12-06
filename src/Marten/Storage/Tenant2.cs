
namespace Marten.Storage
{
    public class Tenant
    {
        public Tenant(string tenantId, IMartenDatabase inner)
        {
            Storage = inner;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IMartenDatabase Storage { get; }
    }
}
