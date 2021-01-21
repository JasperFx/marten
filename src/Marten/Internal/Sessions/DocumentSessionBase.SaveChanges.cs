using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal.Sessions
{
    public abstract partial class DocumentSessionBase
    {

        public void SaveChanges()
        {
            assertNotDisposed();

            processChangeTrackers();
            if (!_workTracker.HasOutstandingWork()) return;

            Database.BeginTransaction();

            Options.Events.ProcessEvents(this);

            _workTracker.Sort(Options);


            foreach (var listener in Listeners) listener.BeforeSaveChanges(this);

            var batch = new UpdateBatch(_workTracker.AllOperations);
            ExecuteBatch(batch);

            resetDirtyChecking();

            EjectPatchedTypes(_workTracker);
            Logger.RecordSavedChanges(this, _workTracker);

            foreach (var listener in Listeners) listener.AfterCommit(this, _workTracker);

            // Need to clear the unit of work here
            _workTracker.Reset();
        }

        internal void ExecuteBatch(IUpdateBatch batch)
        {
            try
            {
                batch.ApplyChanges(this);
                Database.Commit();
            }
            catch (Exception)
            {
                Database.Rollback();

                if (Options.Events.TryCreateTombstoneBatch(this, out var tombstoneBatch))
                {
                    try
                    {
                        tombstoneBatch.ApplyChanges(this);
                    }
                    catch (Exception)
                    {
                        // Failures are logged within the ManagedConnection
                    }
                }

                throw;
            }
        }

        public async Task SaveChangesAsync(CancellationToken token = default)
        {
            assertNotDisposed();

            processChangeTrackers();
            if (!_workTracker.HasOutstandingWork()) return;

            await Database.BeginTransactionAsync(token).ConfigureAwait(false);

            await Options.Events.ProcessEventsAsync(this, token).ConfigureAwait(false);

            _workTracker.Sort(Options);

            foreach (var listener in Listeners)
                await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);

            var batch = new UpdateBatch(_workTracker.AllOperations);

            await ExecuteBatchAsync(batch, token);

            resetDirtyChecking();

            EjectPatchedTypes(_workTracker);
            Logger.RecordSavedChanges(this, _workTracker);

            foreach (var listener in Listeners)
                await listener.AfterCommitAsync(this, _workTracker, token).ConfigureAwait(false);

            // Need to clear the unit of work here
            _workTracker.Reset();
        }

        internal async Task ExecuteBatchAsync(IUpdateBatch batch, CancellationToken token)
        {
            try
            {
                await batch.ApplyChangesAsync(this, token).ConfigureAwait(false);
                await Database.CommitAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await Database.RollbackAsync(token).ConfigureAwait(false);

                if (Options.Events.TryCreateTombstoneBatch(this, out var tombstoneBatch))
                {
                    try
                    {
                        await tombstoneBatch.ApplyChangesAsync(this, token);
                    }
                    catch (Exception)
                    {
                        // Failures are logged within the ManagedConnection
                    }
                }

                throw;
            }
        }
    }
}
