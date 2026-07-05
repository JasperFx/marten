using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Operations;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services.BatchQuerying;

namespace Marten.Events;

internal class RichEventAppender: IEventAppender
{
    public async Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IInlineProjection<IDocumentOperations>[] inlineProjections, CancellationToken token)
    {
        var batch = session.CreateBatchQuery();

        var fetcher = new EventSequenceFetcher(eventGraph, session.WorkTracker.Streams.Sum(x => x.Events.Count));

        var sequencesFetch = batch.AddItem(fetcher);

        var storage = session.EventStorage();
        var streamActions = session.WorkTracker.Streams.Where(x => x.Events.Any()).ToArray();

        var appends = streamActions
            .Where(x => x.ActionType == StreamActionType.Append)
            .Select(x => new RichStreamAppendingStep(x, batch, storage))
            .ToArray();

        await batch.Execute(token).ConfigureAwait(false);

        var sequences = await sequencesFetch.ConfigureAwait(false);

        foreach (var append in appends)
        {
            await append.ApplyAsync(session, eventGraph, sequences, storage, token).ConfigureAwait(false);
        }

        foreach (var stream in streamActions)
        {
            stream.TenantId ??= session.TenantId;

            if (stream.ActionType == StreamActionType.Start)
            {
                // #4424: pass a metadata context whose TenantId matches the
                // stream so applyRichMetadata stamps events with the stream's
                // tenant instead of the session's. See TenantPropagation.
                stream.PrepareEvents(0, eventGraph, sequences,
                    TenantPropagation.MetadataContextFor(session, stream));
                session.QueueOperation(storage.InsertStream(stream));
            }
            else if (eventGraph.UseMandatoryStreamTypeDeclaration && stream.IsStarting())
            {
                throw new NonExistentStreamException(eventGraph.StreamIdentity == StreamIdentity.AsGuid
                    ? stream.Id
                    : stream.Key);
            }

            foreach (var @event in stream.Events)
            {
                session.QueueOperation(storage.AppendEvent(eventGraph, session, stream, @event));
            }

            EventTagOperations.QueueTagOperations(eventGraph, session, stream);

            // #4591: always queue the DCB tag-version producer-bump separately
            // from the per-storage-mode tag writes. Boundary-fetch sessions in
            // flight rely on this row-level bump to invalidate their captured
            // versions.
            EventTagOperations.QueueDcbVersionBumpIfNeeded(eventGraph, session, stream);
        }

        // Queue AssertStreamVersion operations for streams with AlwaysEnforceConsistency but no events
        foreach (var stream in session.WorkTracker.Streams.Where(x => !x.Events.Any() && x.AlwaysEnforceConsistency && x.ExpectedVersionOnServer.HasValue))
        {
            stream.TenantId ??= session.TenantId;

            // The closed-shape storage picks the single-tenant vs conjoined
            // (and Guid vs string identity) operation variant — no per-op
            // tenancy/identity branching here. See #4803.
            session.QueueOperation(storage.AssertStreamVersion(stream));
        }

        // TODO -- look for opportunities to batch up the requests here too!
        // 9.0 (#4306): IInlineProjection.ApplyAsync now takes IEnumerable<StreamAction>,
        // so we can pass the session's tracker collection directly without
        // allocating a fresh List on every SaveChangesAsync.
        //
        // Issue #4481: only pass streams that actually have events. An empty
        // stream (e.g. FetchForWriting<TAggregate> called without any
        // subsequent AppendOne/AppendMany) used to slip through here and
        // trigger an inline projection's snapshot-write path on the unchanged
        // aggregate, raising a JasperFx.ConcurrencyException on the next
        // SaveChangesAsync for any other work on the same session. The
        // upstream JasperFx aggregation base's `AppliesTo(eventTypes)`
        // returns `true` unconditionally when the projection has no
        // statically-known event types (Evolve-only projections), so the
        // empty stream was not being filtered downstream. The lazy `Where`
        // preserves the no-allocation property that #4306 introduced — the
        // filter is one tiny iterator object per projection call instead of
        // a fresh List on every save.
        foreach (var projection in inlineProjections)
        {
            await projection.ApplyAsync(session, session.WorkTracker.Streams.Where(x => x.Events.Any()), token).ConfigureAwait(false);
        }
    }
}

internal class RichStreamAppendingStep: IEventAppendingStep
{
    private readonly StreamAction _stream;
    private readonly Task<StreamState> _fetcher;

    public RichStreamAppendingStep(StreamAction stream, IBatchedQuery query, IEventStorage storage)
    {
        _stream = stream;
        _fetcher = query.AddItem(storage.QueryForStream(stream));
    }

    public async ValueTask ApplyAsync(DocumentSessionBase session, EventGraph eventGraph,
        Queue<long> sequences,
        IEventStorage storage,
        CancellationToken cancellationToken)
    {
        var state = await _fetcher.ConfigureAwait(false);

        _stream.TenantId ??= session.TenantId;
        // #4424: see RichEventAppender — use stream-tenant metadata context.
        var metadataContext = TenantPropagation.MetadataContextFor(session, _stream);

        if (state == null)
        {
            _stream.PrepareEvents(0, eventGraph, sequences, metadataContext);
            session.QueueOperation(storage.InsertStream(_stream));
        }
        else
        {
            if (state.IsArchived)
            {
                throw new InvalidStreamOperationException(
                    $"Attempted to append event to archived stream with Id '{state.Id}'.");
            }

            _stream.PrepareEvents(state.Version, eventGraph, sequences, metadataContext);
            session.QueueOperation(storage.UpdateStreamVersion(_stream));
        }
    }
}
