#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Exceptions;
using JasperFx.Events;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Npgsql;
using Weasel.Core;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    public async Task SaveChangesAsync(CancellationToken token = default)
    {
        assertNotDisposed();

        processChangeTrackers();
        if (!_workTracker.HasOutstandingWork())
        {
            // #5276: a session whose only work was reading a DCB boundary lands here. Nothing is
            // written, so nothing enforces the boundary -- and the read does not carry over to the
            // next unit of work either.
            ForgetPendingDcbBoundaryAssertions();
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

        // #5343: Marten declares IMessageBatch : IMessageSink, IChangeListener -- so it promises BOTH
        // commit hooks -- but only ever called AfterCommitAsync below. The before hook is the half a
        // transactional outbox actually needs, because it is the only point at which an outbox can
        // queue its own operations and have them land in the SAME UpdateBatch, and therefore the same
        // transaction, as the events that produced the messages. Hence the placement: immediately
        // after the session listeners' BeforeSaveChangesAsync and before the UpdateBatch is built
        // from _workTracker, which is exactly the contract those listeners already get.
        //
        // Found by the jasperfx#763 ProjectionSideEffectCompliance suite, whose commit-boundary facts
        // observe what the rest of the database can see at each hook rather than merely that the
        // hooks ran. Wolverine's MartenToWolverineMessageBatch.BeforeCommitAsync is currently a
        // no-op, so nothing downstream changes behaviour on this fix today.
        if (_messageBatch != null)
        {
            await _messageBatch.BeforeCommitAsync(this, _workTracker, token).ConfigureAwait(false);
        }

        var batch = new UpdateBatch(_workTracker.AllOperations);

        await ExecuteBatchAsync(batch, token).ConfigureAwait(false);

        if (_messageBatch != null)
        {
            await _messageBatch.AfterCommitAsync(this, _workTracker, token).ConfigureAwait(false);
            // This is important, we need to throw this away on every commit and start w/ a fresh
            // one on new transactions
            _messageBatch = null;
        }


        resetDirtyChecking();

        EjectPatchedTypes(_workTracker);
        Logger.RecordSavedChanges(this, _workTracker);

        foreach (var listener in Listeners)
        {
            await listener.AfterCommitAsync(this, _workTracker, token).ConfigureAwait(false);
        }

        // Need to clear the unit of work here
        _workTracker.Reset();

        // #5276: and with it any boundary this save read but never appended to.
        ForgetPendingDcbBoundaryAssertions();
    }

    private IMessageBatch? _messageBatch;
    internal virtual async ValueTask<IMessageBatch> StartMessageBatch()
    {
        _messageBatch ??= await Options.Events.MessageOutbox.CreateBatch(this).ConfigureAwait(false);
        return _messageBatch;
    }

    bool IStorageOperations.EnableSideEffectsOnInlineProjections => Options.Events.EnableSideEffectsOnInlineProjections;

    async ValueTask<IMessageSink> IStorageOperations.GetOrStartMessageSink()
    {
        return await StartMessageBatch().ConfigureAwait(false);
    }

    private IEnumerable<Type> operationDocumentTypes()
    {
        // Single-pass HashSet so we don't enumerate Operations() twice (once for
        // Select, once for the Distinct hash) and don't allocate intermediate
        // LINQ enumerator chains on every SaveChanges.
        var types = new HashSet<Type>();
        foreach (var op in _workTracker.Operations())
        {
            var documentType = op.DocumentType;
            if (documentType != null)
            {
                types.Add(documentType);
            }
        }

        return types;
    }

    internal record PagesExecution(IReadOnlyList<OperationPage> Pages, IConnectionLifetime Connection,
        IReadOnlyList<ITransactionParticipant>? Participants)
    {
        public List<Exception> Exceptions { get; } = new();
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

        var pages = batch.BuildPages(this);
        if (!pages.Any())
        {
            // #4685: a rebuild-mode ProjectionUpdateBatch that routed every document insert
            // into its bulk-copy buffer has no command pages but still carries pending COPY
            // work in its transaction participant. Batch-level participants are only ever
            // populated on the daemon path, so this doesn't change inline-session behavior
            // (UpdateBatch.TransactionParticipants is always empty).
            if (batch.TransactionParticipants is not { Count: > 0 })
            {
                return;
            }
        }

        // Merge participants from both the batch (async daemon) and the session (inline projections)
        IReadOnlyList<ITransactionParticipant>? participants = null;
        if (batch.TransactionParticipants is { Count: > 0 } && _transactionParticipants.Count > 0)
        {
            var merged = new List<ITransactionParticipant>(batch.TransactionParticipants);
            merged.AddRange(_transactionParticipants);
            participants = merged;
        }
        else if (batch.TransactionParticipants is { Count: > 0 })
        {
            participants = batch.TransactionParticipants;
        }
        else if (_transactionParticipants.Count > 0)
        {
            participants = _transactionParticipants;
        }
        var execution = new PagesExecution(pages, _connection, participants);

        try
        {
            try
            {

                await executeBeforeCommitListeners(batch).ConfigureAwait(false);

                // #5262: the WRITE pipeline, not the general one. A unit of work carries event appends
                // and is not idempotent, so it may only be replayed when the previous attempt is known
                // to have left nothing behind. See WriteRetryClassifier.
                await Options.WriteResiliencePipeline.ExecuteAsync(
                    static (e, t) => new ValueTask(e.Connection.ExecuteBatchPagesAsync(e.Pages, e.Exceptions, t, e.Participants)), execution, token).ConfigureAwait(false);

                await executeAfterCommitListeners(batch).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (execution.Exceptions.Count == 1)
            {
                var ex = execution.Exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (execution.Exceptions.Any())
            {
                throw new AggregateException(execution.Exceptions);
            }
        }
        catch (Exception)
        {
            await tryApplyTombstoneEventsAsync(token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            // Operations can hold pooled buffers that back their command parameters, and the
            // resilience pipeline above may execute the same batch more than once - so the rentals
            // belong to the unit of work, not to a single execution. See #5262.
            foreach (var page in pages)
            {
                foreach (var operation in page.Operations)
                {
                    if (operation is IDisposable disposable) disposable.Dispose();
                }
            }
        }
    }

    private async Task executeAfterCommitListeners(IUpdateBatch batch)
    {
        try
        {
            await batch.PostUpdateAsync(this).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
        }
    }

    private async Task executeBeforeCommitListeners(IUpdateBatch batch)
    {
        try
        {
            await batch.PreUpdateAsync(this).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
        }
    }

    protected virtual Task tryApplyTombstoneEventsAsync(CancellationToken token)
    {
        if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
        {
            return Options.EventGraph.PostTombstonesAsync(tombstoneBatch);
        }

        return Task.CompletedTask;
    }
}
