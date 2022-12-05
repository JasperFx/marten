#nullable enable
using System;
using Marten.Internal.Storage;
using Marten.Services;

namespace Marten.Internal.Sessions;

public class IdentityMapDocumentSession: DocumentSessionBase
{
    internal IdentityMapDocumentSession(DocumentStore store, SessionOptions sessionOptions,
        IConnectionLifetime connection): base(store, sessionOptions, connection)
    {
    }

    internal override DocumentTracking TrackingMode => DocumentTracking.IdentityOnly;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return provider.IdentityMap;
    }

    protected internal override void ejectById<T>(long id)
    {
        StorageFor<T>().EjectById(this, id);
    }

    protected internal override void ejectById<T>(int id)
    {
        StorageFor<T>().EjectById(this, id);
    }

    protected internal override void ejectById<T>(Guid id)
    {
        StorageFor<T>().EjectById(this, id);
    }

    protected internal override void ejectById<T>(string id)
    {
        StorageFor<T>().EjectById(this, id);
    }
}
