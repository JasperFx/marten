using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Schema.Identity;
using Weasel.Core;

namespace Marten.Events;

public enum EventAppendMode
{
    /// <summary>
    /// Default behavior that ensures that all inline projections will have full access to all event
    /// metadata including intended event sequences, versions, and timestamps
    /// </summary>
    Rich,

    /// <summary>
    /// Stripped down, more performant mode of appending events that will omit some event metadata within
    /// inline projections
    /// </summary>
    Quick
}

public partial class EventGraph
{
    private RetryBlock<UpdateBatch> _tombstones;

    private async Task executeTombstoneBlock(UpdateBatch batch, CancellationToken cancellationToken)
    {
        await using var session = (DocumentSessionBase)(batch.TenantId.IsEmpty()
            ? _store.LightweightSession()
            : _store.LightweightSession(batch.TenantId!));
        await session.ExecuteBatchAsync(batch, cancellationToken).ConfigureAwait(false);
    }

    internal IEventAppender EventAppender { get; set; } = new RichEventAppender();

    public EventAppendMode AppendMode
    {
        get
        {
            return EventAppender is RichEventAppender ? EventAppendMode.Rich : EventAppendMode.Quick;
        }
        set
        {
            EventAppender = value == EventAppendMode.Quick ? new QuickEventAppender() : new RichEventAppender();
        }
    }

    internal void ProcessEvents(DocumentSessionBase session)
    {
        if (!session.WorkTracker.Streams.Any())
        {
            return;
        }

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            session.Database.EnsureStorageExists(typeof(IEvent));
        }

        EventAppender.ProcessEvents(this, session, _inlineProjections.Value);
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

        await EventAppender.ProcessEventsAsync(this, session, _inlineProjections.Value, token).ConfigureAwait(false);
    }

    internal bool TryCreateTombstoneBatch(DocumentSessionBase session, out UpdateBatch batch)
    {
        if (session.WorkTracker.Streams.Any())
        {
            var stream = StreamAction.ForTombstone(session);

            var tombstone = new Tombstone();
            var mapping = EventMappingFor<Tombstone>();

            var operations = new List<IStorageOperation>();
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
                    StreamId = EstablishTombstoneStream.StreamId,
                    StreamKey = EstablishTombstoneStream.StreamKey,
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

    internal void PostTombstones(UpdateBatch tombstoneBatch)
    {
        try
        {
            using var session = (DocumentSessionBase)_store.LightweightSession(tombstoneBatch.TenantId);
            session.ExecuteBatch(tombstoneBatch);
        }
        catch (Exception)
        {
            // The IMartenLogger will log the exception
            _tombstones.Post(tombstoneBatch);
        }
    }

    public Task PostTombstonesAsync(UpdateBatch tombstoneBatch)
    {
        return _tombstones.PostAsync(tombstoneBatch);
    }
}
