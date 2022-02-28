
using System.Collections.Generic;
using Marten.Storage;

#nullable enable
namespace Marten.Internal.Sessions
{
    public partial class QuerySession
    {
        private Dictionary<string, NestedTenantQuerySession>? _byTenant;

        public ITenantQueryOperations ForTenant(string tenantId)
        {
            _byTenant ??= new Dictionary<string, NestedTenantQuerySession>();

            if (_byTenant.TryGetValue(tenantId, out var tenantSession))
            {
                return tenantSession;
            }

            var tenant = new Tenant(tenantId, Database);
            tenantSession = new NestedTenantQuerySession(this, tenant);
            _byTenant[tenantId] = tenantSession;

            return tenantSession;
        }
    }
}
