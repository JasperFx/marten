using System;
using System.Linq;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal.Sessions
{
    public class DirtyCheckingDocumentSession: DocumentSessionBase
    {
        public DirtyCheckingDocumentSession(DocumentStore store, SessionOptions sessionOptions, IManagedConnection database, ITenant tenant) : base(store, sessionOptions, database, tenant)
        {
        }

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return provider.DirtyTracking;
        }

        protected internal override void processChangeTrackers()
        {
            foreach (var tracker in ChangeTrackers)
            {
                if (tracker.DetectChanges(this, out var operation))
                {
                    _workTracker.Add(operation);
                }
            }
        }

        protected internal override void resetDirtyChecking()
        {
            foreach (var tracker in ChangeTrackers)
            {
                tracker.Reset(this);
            }

            var knownDocuments = ChangeTrackers.Select(x => x.Document).ToArray();

            var operations = _workTracker.AllOperations
                .OfType<IDocumentStorageOperation>()
                .Where(x => !knownDocuments.Contains(x.Document));

            foreach (var operation in operations)
            {
                var tracker = operation.ToTracker(this);
                ChangeTrackers.Add(tracker);
            }
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
}
