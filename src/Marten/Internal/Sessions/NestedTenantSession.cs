#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Core;
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
        ItemMap = parent.ItemMap;
    }

    public IDocumentSession Parent => _parent;

    internal override ValueTask<IMessageBatch> CurrentMessageBatch()
    {
        return _parent.CurrentMessageBatch();
    }

    protected internal override void ejectById<T>(long id)
    {
        _parent.ejectById<T>(id);
    }

    protected internal override void ejectById<T>(int id)
    {
        _parent.ejectById<T>(id);
    }

    protected internal override void ejectById<T>(Guid id)
    {
        _parent.ejectById<T>(id);
    }

    protected internal override void ejectById<T>(string id)
    {
        _parent.ejectById<T>(id);
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
}
