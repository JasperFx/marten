#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Npgsql;
using Weasel.Core;

namespace Marten.Internal.Sessions;

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

        try
        {
            Options.EventGraph.ProcessEvents(this);
        }
        catch (Exception)
        {
            tryApplyTombstoneBatch();
            throw;
        }

        _workTracker.Sort(Options);

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var operationType in operationDocumentTypes()) Database.EnsureStorageExists(operationType);
        }

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

    public async Task SaveChangesAsync(CancellationToken token = default)
    {
        assertNotDisposed();

        processChangeTrackers();
        if (!_workTracker.HasOutstandingWork())
        {
            return;
        }

        try
        {
            await Options.EventGraph.ProcessEventsAsync(this, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await tryApplyTombstoneEventsAsync(token).ConfigureAwait(false);

            throw;
        }

        _workTracker.Sort(Options);

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var operationType in operationDocumentTypes())
            {
                await Database.EnsureStorageExistsAsync(operationType, token).ConfigureAwait(false);
            }
        }

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

    private IEnumerable<Type> operationDocumentTypes()
    {
        return _workTracker.Operations().Select(x => x.DocumentType).Where(x => x != null).Distinct();
    }

    internal void ExecuteBatch(IUpdateBatch batch)
    {
        var exceptions = new List<Exception>();
        var pages = batch.BuildPages(this);

        try
        {
            try
            {
                _connection.ExecuteBatchPages(pages, Logger, exceptions);
            }
            catch (Exception e)
            {
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }
        catch (Exception)
        {
            tryApplyTombstoneBatch();

            throw;
        }
    }

    private void tryApplyTombstoneBatch()
    {
        if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
        {
            Options.EventGraph.PostTombstones(tombstoneBatch);
        }
    }

    internal async Task ExecuteBatchAsync(IUpdateBatch batch, CancellationToken token)
    {
        // TODO -- double check this isn't getting done multiple times
        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var documentType in batch.DocumentTypes())
            {
                await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
            }
        }

        var exceptions = new List<Exception>();
        var pages = batch.BuildPages(this);
        if (!pages.Any()) return;

        try
        {
            try
            {
                await _connection.ExecuteBatchPagesAsync(pages, Logger, exceptions, token).ConfigureAwait(false);

                try
                {
                    await batch.PostUpdateAsync(this).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(new NpgsqlCommand(), e);
                }
            }
            catch (Exception e)
            {
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }
        catch (Exception)
        {
            await tryApplyTombstoneEventsAsync(token).ConfigureAwait(false);
            throw;
        }
    }

    private Task tryApplyTombstoneEventsAsync(CancellationToken token)
    {
        if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
        {
            return Options.EventGraph.PostTombstonesAsync(tombstoneBatch);
        }

        return Task.CompletedTask;
    }
}
