using JasperFx.Core;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions;

internal class NestedTenantQuerySession: QuerySession, ITenantQueryOperations
{
    private readonly QuerySession _parent;

    internal NestedTenantQuerySession(QuerySession parent, Tenant tenant): base((DocumentStore)parent.DocumentStore,
        parent.SessionOptions, parent._connection, tenant)
    {
        Listeners.AddRange(parent.Listeners);
        _parent = parent;

        // #4801 — Keep identity-map and version state tenant-scoped; only share the
        // parent's when this nested session targets the parent's own tenant. See
        // NestedTenantSession for the full rationale.
        if (tenant.TenantId == parent.TenantId)
        {
            ItemMap = parent.ItemMap;
            Versions = parent.Versions;
        }
    }

    public IQuerySession Parent => _parent;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        var storage = _parent.selectStorage(provider);
        this.ShareTenantNeutralStateWith(_parent, storage);
        return storage;
    }
}
