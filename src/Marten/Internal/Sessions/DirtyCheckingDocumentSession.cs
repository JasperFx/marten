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
        var explicitRevisions = documentsWithAnExplicitRevision();
        if (explicitRevisions == null)
        {
            DirtyTrackingSweep.DetectChanges(ChangeTrackers, this, _workTracker.Add);
            return;
        }

        DirtyTrackingSweep.DetectChanges(ChangeTrackers, this, operation =>
        {
            if (operation is IDocumentStorageOperation detected && explicitRevisions.Contains(detected.Document))
            {
                return;
            }

            _workTracker.Add(operation);
        });
    }

    // #5788: a detected change is an upsert at the auto revision, and UnitOfWork.Add replaces any pending
    // operation for the same document instance. An explicit numeric revision -- Store() on a document
    // carrying a non-zero revision, or UpdateRevision / TryUpdateRevision -- lives only on that pending
    // operation's Revision, so letting the sweep replace it threw the revision (and with it the concurrency
    // guard) away: a loaded-then-changed document always took the auto path. The explicit operation
    // serializes the document when its command is built, so it already writes the detected change.
    // (Guid-versioned operations are unaffected: their expected version is read from the session's version
    // map, which a replacement operation sees just the same.)
    private HashSet<object>? documentsWithAnExplicitRevision()
    {
        HashSet<object>? documents = null;
        foreach (var operation in _workTracker.AllOperations)
        {
            if (operation is IDocumentStorageOperation pending and IRevisionedOperation { Revision: not 0 })
            {
                documents ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
                documents.Add(pending.Document);
            }
        }

        return documents;
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
