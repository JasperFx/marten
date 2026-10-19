using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Blocks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;

namespace Marten.Events;

public partial class EventGraph
{
    private RetryBlock<UpdateBatch> _tombstones;

    internal IEventAppender EventAppender { get; set; } = new RichEventAppender();

    public override EventAppendMode AppendMode
    {
        get => base.AppendMode;
        set
        {
            base.AppendMode = value;
            EventAppender = value == EventAppendMode.Rich ? new RichEventAppender() : new QuickEventAppender();
        }
    }

    private async Task executeTombstoneBlock(UpdateBatch batch, CancellationToken cancellationToken)
    {
        await using var session = (DocumentSessionBase)(batch.TenantId.IsEmpty()
            ? _store.LightweightSession()
            : _store.LightweightSession(batch.TenantId!));
        await session.ExecuteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ProcessEventsAsync(DocumentSessionBase session, CancellationToken token)
    {
        if (!session._workTracker.Streams.Any())
        {
            return;
        }

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            await session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        }

        // #5276: hand any DCB boundary the session captured to the unit of work, but only if this
        // save actually appends events -- the boundary is a condition on an append, and a save that
        // appends nothing has to leave mt_dcb_tag_version exactly as it found it. Done here rather
        // than at fetch time, and *before* the appender, so the assertion still runs ahead of the
        // producer-side bumps it would otherwise read as a conflict.
        session.QueuePendingDcbBoundaryAssertions();

        // Before the appender and the listeners, so an IDocumentSessionListener can ask
        // IEventStoreOperations.ColdVersionOf(stream) for any appended stream.
        await session.ResolveColdVersionsAsync(token).ConfigureAwait(false);

        await EventAppender.ProcessEventsAsync(this, session, _inlineProjections.Value, token).ConfigureAwait(false);

        if (UseListenNotifyForEventAppends)
        {
            session.QueueOperation(new NotifyEventAppendedOperation());
        }
    }

    internal bool TryCreateTombstoneBatch(DocumentSessionBase session, [NotNullWhen(true)]out UpdateBatch? batch)
    {
        if (session.WorkTracker.Streams.Any())
        {
            var stream = StreamAction.ForTombstone(session);

            var tombstone = new Tombstone();
            var mapping = EventMappingFor<Tombstone>();

            // #4821 event E3: closed-shape event ops implement only the neutral
            // Weasel.Storage.IStorageOperation; UpdateBatch already takes the neutral list.
            var operations = new List<Weasel.Storage.IStorageOperation>();
            var storage = session.EventStorage();

            operations.Add(new EstablishTombstoneStream(this, session.TenantId));
            var tombstones = session.WorkTracker.Streams
                .SelectMany(x => x.ToTombstoneEvents(mapping, tombstone))
                .Where(x => x.Sequence != 0) // don't even try to save a tombstone if you don't know the sequence
                .Select(x => new Event<Tombstone>(tombstone)
                {
                    Sequence = x.Sequence,
                    Version = x.Sequence, // this is important to avoid clashes on the id/version constraint
                    TenantId = x.TenantId,
                    StreamId = Tombstone.StreamId,
                    StreamKey = Tombstone.StreamKey,
                    Id = CombGuidIdGeneration.NewGuid(),
                    EventTypeName = mapping.EventTypeName,
                    DotNetTypeName = mapping.DotNetTypeName
                })
                .Select(e => storage.AppendEvent(this, session, stream, e));

            operations.AddRange(tombstones);

            batch = new UpdateBatch(operations) { TenantId = session.TenantId };

            return true;
        }

        batch = null;
        return false;
    }

    public Task PostTombstonesAsync(UpdateBatch tombstoneBatch)
    {
        return _tombstones.PostAsync(tombstoneBatch);
    }

    private readonly object _pendingTombstoneLock = new();
    private readonly HashSet<Task> _pendingTombstones = new();

    /// <summary>
    ///     #5688. Writes the tombstones for a failed unit of work off the caller's path: the save that failed
    ///     throws straight away instead of waiting on this write.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tombstones fill sequence numbers the failed unit of work drew, on a fresh connection, so they
    ///         can only land once nothing holds those numbers any more. Awaiting them inline made the save wait
    ///         on its own transaction whenever that transaction was still open -- a caller-owned or ambient one,
    ///         which Marten deliberately does not roll back, or one a cancelled token kept from being rolled
    ///         back -- until the command timeout broke the wait.
    ///     </para>
    ///     <para>
    ///         The write does not run on the caller's <see cref="ExecutionContext" />, so an ambient
    ///         <c>TransactionScope</c> cannot flow into the tombstone session and enlist it in the very
    ///         transaction that failed. The retry block still retries a first attempt that fails.
    ///     </para>
    /// </remarks>
    internal void EnqueueTombstones(UpdateBatch tombstoneBatch)
    {
        Task write;
        using (ExecutionContext.SuppressFlow())
        {
            write = Task.Run(() => _tombstones.PostAsync(tombstoneBatch), CancellationToken.None);
        }

        lock (_pendingTombstoneLock)
        {
            _pendingTombstones.Add(write);
        }

        write.ContinueWith(t =>
        {
            _ = t.Exception; // observed; the retry block has already logged it

            lock (_pendingTombstoneLock)
            {
                _pendingTombstones.Remove(t);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    ///     #5688. Completes once the first attempt of every tombstone write queued so far has finished --
    ///     the point a failed save used to return at, before the write moved off its path. For tests, and for
    ///     store disposal.
    /// </summary>
    internal async Task WaitForPendingTombstonesAsync()
    {
        Task[] pending;
        lock (_pendingTombstoneLock)
        {
            pending = _pendingTombstones.ToArray();
        }

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Observed in EnqueueTombstones; a failed first attempt belongs to the retry block now
        }
    }
}
