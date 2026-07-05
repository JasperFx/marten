#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions;

internal class NestedTenantSession: DocumentSessionBase, ITenantOperations
{
    private readonly DocumentSessionBase _parent;

    internal NestedTenantSession(DocumentSessionBase parent, Tenant tenant): base((DocumentStore)parent.DocumentStore,
        parent.SessionOptions, parent._connection, parent._workTracker, tenant)
    {
        Listeners.AddRange(parent.Listeners);
        _parent = parent;
        Versions = parent.Versions;

        // #4801 — Do NOT share the parent's identity map. Under conjoined tenancy the
        // same id maps to a different document per tenant, so a shared, tenant-blind
        // ItemMap would return one tenant's cached instance for another tenant. ForTenant
        // caches one nested session per tenant, so each tenant keeps its own map across
        // repeated ForTenant(tenant) calls. The base session already initializes ItemMap
        // to a fresh dictionary, so we simply leave it un-shared here.
    }

    public IDocumentSession Parent => _parent;

    // #4801 — Eject from THIS nested session's own identity map (and any dirty tracker),
    // not the parent's. The map is no longer shared with the parent, so delegating the
    // eject to the parent would leave this tenant's cached instance in place. Calling
    // RemoveDirtyTracker is a harmless no-op when the parent is not dirty-tracking.
    protected internal override void ejectById<T>(long id)
    {
        ejectFromThisSession<T>(id);
    }

    protected internal override void ejectById<T>(int id)
    {
        ejectFromThisSession<T>(id);
    }

    protected internal override void ejectById<T>(Guid id)
    {
        ejectFromThisSession<T>(id);
    }

    protected internal override void ejectById<T>(string id)
    {
        ejectFromThisSession<T>(id);
    }

    private void ejectFromThisSession<T>(object id) where T : notnull
    {
        var storage = StorageFor<T>();
        storage.EjectById(this, id);
        storage.RemoveDirtyTracker(this, id);
    }

    protected internal override void processChangeTrackers()
    {
        _parent.processChangeTrackers();
    }

    protected internal override void resetDirtyChecking()
    {
        _parent.resetDirtyChecking();
    }

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return _parent.selectStorage(provider);
    }

    public override void Dispose()
    {
        // NOTHING
    }

    public override ValueTask DisposeAsync()
    {
        // Do nothing!
        return ValueTask.CompletedTask;
    }

    internal override ValueTask<IMessageBatch> StartMessageBatch()
    {
        return Parent.As<DocumentSessionBase>().StartMessageBatch();
    }
}
