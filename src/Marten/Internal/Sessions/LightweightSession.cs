#nullable enable
using System;
using Marten.Internal.Storage;
using Marten.Services;

namespace Marten.Internal.Sessions;

public class LightweightSession: DocumentSessionBase
{
    internal LightweightSession(DocumentStore store, SessionOptions sessionOptions, IConnectionLifetime connection):
        base(store, sessionOptions, connection)
    {
    }

    internal override DocumentTracking TrackingMode => DocumentTracking.None;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return provider.Lightweight;
    }

    public override void Eject<T>(T document)
    {
        _workTracker.Eject(document);
    }

    public override void EjectAllOfType(Type type)
    {
        _workTracker.EjectAllOfType(type);
    }

    protected internal override void ejectById<T>(long id)
    {
        // Nothing
    }

    protected internal override void ejectById<T>(int id)
    {
        // Nothing
    }

    protected internal override void ejectById<T>(Guid id)
    {
        // Nothing
    }

    protected internal override void ejectById<T>(string id)
    {
        // Nothing
    }

    public override void UseIdentityMapFor<T>()
    {
        var documentStorage = _providers.StorageFor<T>().IdentityMap;
        overrideStorage(typeof(T), documentStorage);
    }
}
