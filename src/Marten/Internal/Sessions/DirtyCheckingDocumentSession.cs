#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Services;
using Weasel.Storage;

namespace Marten.Internal.Sessions;

public class DirtyCheckingDocumentSession: DocumentSessionBase
{
    internal DirtyCheckingDocumentSession(DocumentStore store, SessionOptions sessionOptions,
        IConnectionLifetime connection): base(store, sessionOptions, connection)
    {
    }

    internal override DocumentTracking TrackingMode => DocumentTracking.DirtyTracking;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return provider.DirtyTracking;
    }

    // #5733: the detect and re-baseline passes are Weasel.Storage's DirtyTrackingSweep, the same
    // two loops Polecat and Fisher used to carry. Indexed rather than foreach, because detecting a
    // change resolves storage and can append to the same list.
    protected internal override void processChangeTrackers()
    {
        DirtyTrackingSweep.DetectChanges(ChangeTrackers, this, _workTracker.Add);
    }

    protected internal override void resetDirtyChecking()
    {
        DirtyTrackingSweep.Reset(ChangeTrackers, this, _workTracker.AllOperations);
    }


    private void removeTrackerFor<T>(T document)
    {
        ChangeTrackers.RemoveAll(x => ReferenceEquals(x.Document, document));
    }

    // NEED TO REMOVE TRACKER TOO!

    protected internal override void ejectById<T>(long id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(int id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(Guid id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(string id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }
}
