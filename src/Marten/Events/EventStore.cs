#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Protected;
using Marten.Internal.Sessions;
using Marten.Storage;

namespace Marten.Events;

internal partial class EventStore: QueryEventStore, IEventStoreOperations
{
    private readonly DocumentSessionBase _session;
    private readonly DocumentStore _store;

    public EventStore(DocumentSessionBase session, DocumentStore store, Tenant tenant): base(session, store, tenant)
    {
        _session = session;
        _store = store;
    }

    public void OverwriteEvent(IEvent e)
    {
        var op = new OverwriteEventOperation(_store.Events, e);
        _session.QueueOperation(op);
    }


}
