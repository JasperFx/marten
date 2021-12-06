using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;

#nullable enable
namespace Marten.Internal.Sessions
{
    public abstract partial class DocumentSessionBase
    {
        public void SaveChanges()
        {
            assertNotDisposed();

            processChangeTrackers();
            if (!_workTracker.HasOutstandingWork())
            {
                return;
            }

            BeginTransaction();

            Options.EventGraph.ProcessEvents(this);
            _workTracker.Sort(Options);

            foreach (var listener in Listeners)
            {
                listener.BeforeSaveChanges(this);
            }

            var batch = new UpdateBatch(_workTracker.AllOperations);
            ExecuteBatch(batch);

            resetDirtyChecking();

            EjectPatchedTypes(_workTracker);
            Logger.RecordSavedChanges(this, _workTracker);

            foreach (var listener in Listeners)
            {
                listener.AfterCommit(this, _workTracker);
            }

            // Need to clear the unit of work here
            _workTracker.Reset();
        }

        public async Task SaveChangesAsync(CancellationToken token = default)
        {
            assertNotDisposed();

            processChangeTrackers();
            if (!_workTracker.HasOutstandingWork())
            {
                return;
            }

            await BeginTransactionAsync(token).ConfigureAwait(false);

            await Options.EventGraph.ProcessEventsAsync(this, token).ConfigureAwait(false);
            _workTracker.Sort(Options);


            foreach (var listener in Listeners)
            {
                await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);
            }

            var batch = new UpdateBatch(_workTracker.AllOperations);

            await ExecuteBatchAsync(batch, token).ConfigureAwait(false);

            resetDirtyChecking();

            EjectPatchedTypes(_workTracker);
            Logger.RecordSavedChanges(this, _workTracker);

            foreach (var listener in Listeners)
            {
                await listener.AfterCommitAsync(this, _workTracker, token).ConfigureAwait(false);
            }

            // Need to clear the unit of work here
            _workTracker.Reset();
        }

        internal void ExecuteBatch(IUpdateBatch batch)
        {
            try
            {
                batch.ApplyChanges(this);
                _retryPolicy.Execute(_connection.Commit);
            }
            catch (Exception)
            {
                try
                {
                    _retryPolicy.Execute(_connection.Rollback);
                }
                catch (RollbackException e)
                {
                    if (e.InnerException != null)
                        Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(new NpgsqlCommand(), e);
                }

                if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
                {
                    try
                    {
                        tombstoneBatch.ApplyChanges(this);
                        _retryPolicy.Execute(_connection.Commit);
                    }
                    catch (Exception e)
                    {
                        Logger.LogFailure(new NpgsqlCommand(), e);
                    }
                }

                throw;
            }
        }

        internal async Task ExecuteBatchAsync(IUpdateBatch batch, CancellationToken token)
        {
            try
            {
                await batch.ApplyChangesAsync(this, token).ConfigureAwait(false);
                await _retryPolicy.ExecuteAsync(() => _connection.CommitAsync(token), token).ConfigureAwait(false);

            }
            catch (Exception)
            {
                try
                {
                    await _retryPolicy.ExecuteAsync(() => _connection.RollbackAsync(token), token).ConfigureAwait(false);
                }
                catch (RollbackException e)
                {
                    if (e.InnerException != null)
                        Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(new NpgsqlCommand(), e);
                }

                if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
                {
                    try
                    {
                        await tombstoneBatch.ApplyChangesAsync(this, token).ConfigureAwait(false);
                        await _retryPolicy.ExecuteAsync(() => _connection.CommitAsync(token), token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.LogFailure(new NpgsqlCommand(), e);
                    }
                }

                throw;
            }
        }
    }
}
