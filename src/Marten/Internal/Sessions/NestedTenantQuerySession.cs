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
        Versions = parent.Versions;

        // #4801 — Do NOT share the parent's identity map; keep it tenant-scoped. See
        // NestedTenantSession for the full rationale. The base session already initializes
        // ItemMap to a fresh dictionary, so leaving it un-shared is all that's needed.
    }

    public IQuerySession Parent => _parent;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return _parent.selectStorage(provider);
    }
}
