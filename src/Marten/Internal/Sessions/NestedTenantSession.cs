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

        // #4801 — Under conjoined tenancy the same id maps to a different document per
        // tenant, so tenant-blind identity/version state keyed only by id would return or
        // assert one tenant's state for another tenant. Only share the parent's identity
        // map and version tracker when this nested session targets the parent's own tenant
        // (so ForTenant(sameTenant) stays consistent with direct session access); otherwise
        // keep the base session's fresh, tenant-isolated state. ForTenant caches one nested
        // session per tenant, so that state persists across repeated ForTenant(tenant) calls.
        if (tenant.TenantId == parent.TenantId)
        {
            ItemMap = parent.ItemMap;
            Versions = parent.Versions;
        }
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
        var storage = _parent.selectStorage(provider);
        this.ShareTenantNeutralStateWith(_parent, storage);
        return storage;
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
