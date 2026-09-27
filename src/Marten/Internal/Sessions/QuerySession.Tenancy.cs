#nullable enable
using System.Collections.Generic;
using JasperFx.MultiTenancy;
using Marten.Storage;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    private Dictionary<string, NestedTenantQuerySession>? _byTenant;

    public ITenantQueryOperations ForTenant(string tenantId)
    {
        // #5516, the read-side twin of DocumentSessionBase.ForTenant: the raw id keyed the cache and built
        // the Tenant the nested session filters by, so a mixed-case id under ForceLowerCase queried for a
        // tenant_id that nothing writes.
        tenantId = Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);

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
