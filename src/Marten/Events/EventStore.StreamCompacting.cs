#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Protected;
using Marten.Events.Operations;
using Marten.Events.Protected;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events;

internal partial class EventStore
{
    public Guid CompletelyReplaceEvent<T>(long sequence, T eventBody) where T : class
    {
        var op = new ReplaceEventOperation<T>(_store.Events, eventBody, sequence);
        _session.QueueOperation(op);

        return op.Id;
    }

    public async Task CompactStreamAsync<T>(string streamKey, Action<StreamCompactingRequest<T>>? configure = null)
        where T : class
    {
        // Reject the wrong overload the same way every other identity-sensitive entry point does
        // (see EventStore.ConcurrentAppends and EventStore.FetchForWriting). Without this the
        // request carries a null StreamId into ExecuteAsync, which branches on the store's
        // configured StreamIdentity rather than on which overload was called, and the caller got
        // "Nullable object must have a value" from request.StreamId!.Value -- an error that names
        // nothing the caller can act on.
        _store.Events.EnsureAsStringStorage(_session);

        // The same refusal Append and StartStream make. Compaction rewrites one event and deletes the rest,
        // so it must never be pointed at anything wider than a stream the caller named.
        if (string.IsNullOrEmpty(streamKey))
        {
            throw new ArgumentOutOfRangeException(nameof(streamKey), "The stream key cannot be null or empty");
        }

        var request = new StreamCompactingRequest<T>(streamKey);
        configure?.Invoke(request);

        await MartenStreamCompacting.Instance.CompactAsync(request, _session).ConfigureAwait(false);
    }

    public async Task CompactStreamAsync<T>(Guid streamId, Action<StreamCompactingRequest<T>>? configure = null)
        where T : class
    {
        // The mirror case is worse than a bad message: on a string-identified store ExecuteAsync
        // takes the AsString branch, reads a null StreamKey, matches no stream and returns at the
        // `if (!events.Any()) return;` guard -- so compaction silently did nothing at all.
        _store.Events.EnsureAsGuidStorage(_session);

        // See the string overload above
        if (streamId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(streamId), "Cannot use an empty Guid as the stream id");
        }

        var request = new StreamCompactingRequest<T>(streamId);
        configure?.Invoke(request);

        await MartenStreamCompacting.Instance.CompactAsync(request, _session).ConfigureAwait(false);
    }
}

/// <summary>
///     Marten's half of the shared <see cref="StreamCompactingExecution{TSession}"/> (jasperfx#1048): the
///     fold, the archiver closures Marten can call, and the delete and watermark operations. The read, the
///     order everything is queued in and the refusals are the shared base's.
/// </summary>
internal sealed class MartenStreamCompacting: StreamCompactingExecution<DocumentSessionBase>
{
    public static readonly MartenStreamCompacting Instance = new();

    private MartenStreamCompacting()
    {
    }

    public async Task CompactAsync<T>(StreamCompactingRequest<T> request, DocumentSessionBase session) where T : class
    {
        var options = ((IMartenSession)session).Options;
        var logger = options.LogFactory?.CreateLogger<StreamCompactingRequest<T>>() ??
                     options.DotNetLogger ?? NullLogger<StreamCompactingRequest<T>>.Instance;

        // Built ahead of the read, so a type with no aggregation conventions is refused before anything else
        FindAggregator<T>(session);

        if (await ExecuteAsync(request, session).ConfigureAwait(false))
        {
            logger.LogInformation("Compacted stream {Id} from Version {Version} and down",
                (object?)request.StreamId ?? request.StreamKey, request.Version);
        }
        else
        {
            logger.LogInformation(
                "Left stream {Id} alone: there was nothing to compact, or its events fold to no {AggregateType}",
                (object?)request.StreamId ?? request.StreamKey, typeof(T).FullNameInCode());
        }
    }

    protected override IEventStoreOperations EventsFor(DocumentSessionBase session) => session.Events;

    protected override async ValueTask<T?> BuildAggregateAsync<T>(DocumentSessionBase session,
        IReadOnlyList<IEvent> events, CancellationToken token) where T : class
    {
        return await FindAggregator<T>(session).BuildAsync(events, session, default, token).ConfigureAwait(false);
    }

    // Both closures are accepted, as Polecat does (polecat#423). IEventsArchiver<T> is invariant, so an
    // IEventsArchiver<IDocumentSession> is not an IEventsArchiver<IDocumentOperations>.
    protected override async Task<bool> TryArchiveAsync<T>(DocumentSessionBase session,
        StreamCompactingRequest<T> request, IReadOnlyList<IEvent> events) where T : class
    {
        return await TryArchiveThroughAsync<IDocumentSession, T>(session, request, events).ConfigureAwait(false)
               || await TryArchiveThroughAsync<IDocumentOperations, T>(session, request, events).ConfigureAwait(false);
    }

    protected override void DeleteEvents(DocumentSessionBase session, long[] sequences)
    {
        session.QueueOperation(new DeleteEventsOperation(sequences));
    }

    // jasperfx#740 (marten#5333): record the compaction watermark on mt_streams so
    // StreamState.CompactedVersion -- and the (Version - CompactedVersion) growth measure the
    // Stream Compaction Policies are built on -- reads truthfully.
    protected override void RecordCompactionWatermark(DocumentSessionBase session, Guid? streamId,
        string? streamKey, long version)
    {
        session.QueueOperation(new RecordCompactionWatermarkOperation(
            ((IMartenSession)session).Options.EventGraph, streamId, streamKey, version));
    }

    /// <summary>
    /// The fold used to build the <see cref="Compacted{T}"/> snapshot.
    ///
    /// <para>
    /// #5366 / jasperfx#800: this used to call <c>TryFindAggregate</c>, the registration-only
    /// lookup, and refuse anything not already registered as an aggregation projection. That made
    /// Marten the odd store out — Fisher and Polecat both infer the fold — and imposed a constraint
    /// nobody would infer from the API: a compaction policy could only ever target an aggregate the
    /// application already snapshots. It is a portability gap rather than a safety property, and it
    /// was invisible until runtime.
    /// </para>
    ///
    /// <para>
    /// <c>AggregatorFor&lt;T&gt;</c> is the on-demand lookup sitting right beside it on the shared
    /// <c>ProjectionGraph</c>: registered projections first, then a live aggregator built from
    /// <typeparamref name="T"/>'s own <c>Create</c>/<c>Apply</c> conventions and cached. Every other
    /// aggregation path in Marten already calls it — <c>AggregateToExtensions</c>, the fetch plans,
    /// <c>QueryEventStore</c>, <c>RehydrateAtVersionAsync</c> — so compaction was the outlier, most
    /// likely by accident. The typed overload names <typeparamref name="T"/> outright, which is the
    /// same declaration of intent a registration would be.
    /// </para>
    ///
    /// <para>
    /// A type with no aggregation conventions at all still fails, and must: compaction DELETES the
    /// events it folds, so an empty snapshot would be the worst outcome available here. The refusal
    /// now comes out of the aggregator build rather than from a registration check, so it is
    /// rephrased to name what is actually missing.
    /// </para>
    /// </summary>
    private static IAggregator<T, IQuerySession> FindAggregator<T>(DocumentSessionBase session) where T : class
    {
        try
        {
            return ((IMartenSession)session).Options.Projections.AggregatorFor<T>();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Unable to build an aggregation for type {typeof(T).FullNameInCode()} in order to compact this stream. " +
                "Compaction folds the stream into a single Compacted<T> event and deletes the events it folded, so it " +
                "cannot proceed without a usable aggregation. Give the type Create/Apply methods for its events, or " +
                "register an aggregation projection for it.", e);
        }
    }
}

#region sample_ieventsarchiver

/// <summary>
/// Implement <see cref="IEventsArchiver{TOperations}"/> from
/// <c>JasperFx.Events.Protected</c> with <c>TOperations</c> closed over Marten's
/// <see cref="IDocumentOperations"/> to intercept stream-compaction events
/// before they are permanently deleted. Wire your archiver onto the request via
/// <c>configure.Archiver = new MyArchiver()</c> inside the
/// <c>CompactStreamAsync&lt;T&gt;(id, configure)</c> callback.
/// </summary>
/// <remarks>
/// Pre-2026 Marten shipped its own non-generic <c>Marten.Events.IEventsArchiver</c>
/// interface. That contract has been lifted to
/// <see cref="IEventsArchiver{TOperations}"/> in <c>JasperFx.Events.Protected</c>
/// per the dedupe pillar so Marten and Polecat share a single contract — Marten
/// archivers now close the generic over <see cref="IDocumentOperations"/> or
/// <see cref="IDocumentSession"/>; Polecat archivers close it over the same two types of its own.
/// Marten refuses to compact with an archiver closed over anything else.
/// </remarks>
public sealed class SampleArchiverDocumentation : IEventsArchiver<IDocumentOperations>
{
    public Task MaybeArchiveAsync<T>(
        IDocumentOperations operations,
        StreamCompactingRequest<T> request,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation) where T : class
    {
        // Copy the to-be-deleted events to cold storage, emit an audit record, etc.
        // The compactor will not proceed until this callback completes.
        return Task.CompletedTask;
    }
}

#endregion

/// <summary>
///     jasperfx#740 (marten#5333): stamp the stream-compaction watermark onto
///     <c>mt_streams.compacted_version</c> as part of the same unit of work that writes the
///     <c>Compacted&lt;T&gt;</c> snapshot and deletes the folded events. <c>greatest()</c> keeps the
///     watermark monotonic — a replayed or lower-cutoff compaction request can never move it
///     backwards, which would make a stream's un-compacted growth read larger than it is.
/// </summary>
internal class RecordCompactionWatermarkOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly Guid? _streamId;
    private readonly string? _streamKey;
    private readonly long _version;

    public RecordCompactionWatermarkOperation(EventGraph events, Guid? streamId, string? streamKey, long version)
    {
        _events = events;
        _streamId = streamId;
        _streamKey = streamKey;
        _version = version;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(
            $"update {_events.DatabaseSchemaName}.{Schema.StreamsTable.TableName} set {Schema.StreamsTable.CompactedVersionColumn} = greatest({Schema.StreamsTable.CompactedVersionColumn}, ");
        builder.AppendParameter(_version);
        builder.Append(") where id = ");

        if (_events.StreamIdentity == StreamIdentity.AsGuid)
        {
            builder.AppendParameter(_streamId!.Value);
        }
        else
        {
            builder.AppendParameter(_streamKey!);
        }

        // Same #5234 discipline as the sibling compaction operations: under conjoined tenancy the
        // same stream id exists in every tenant, and only the calling tenant's watermark may move.
        builder.AppendConjoinedTenantFilter(session);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}

internal class DeleteEventsOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly long[] _sequences;

    public DeleteEventsOperation(long[] sequences)
    {
        _sequences = sequences;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var events = ((IMartenSession)session).Options.EventGraph;
        var schema = events.DatabaseSchemaName;

        // The DCB tag rows of the folded events go in the same statement, ahead of the events. Each
        // mt_event_tag_* table has a foreign key to mt_events that does NOT cascade, so deleting a tagged
        // event while its tag rows remain is refused with 23503. Same shape as DeleteStreamOperation: the
        // CTEs share one snapshot and the key is checked at the end of the statement, when both are gone.
        // HStore mode keeps tags inline on mt_events and has no tag tables.
        IReadOnlyList<JasperFx.Events.Tags.ITagTypeRegistration> tagTypes =
            events.DcbStorageMode == DcbStorageMode.HStore ? [] : events.TagTypes;

        for (var i = 0; i < tagTypes.Count; i++)
        {
            builder.Append(i == 0 ? "with " : ", ");
            builder.Append($"tag{i} as (delete from {schema}.mt_event_tag_{tagTypes[i].TableSuffix} where seq_id = ANY(");
            builder.AppendParameter(_sequences);
            builder.Append(")");

            // The tag tables carry tenant_id under conjoined tenancy, and seq_id alone is not an event
            // there (#5234, below)
            builder.AppendConjoinedTenantFilter(session);
            builder.Append(") ");
        }

        builder.Append($"delete from {schema}.mt_events where seq_id = ANY(");
        builder.AppendParameter(_sequences);
        builder.Append(")");

        // #5234: without this, compacting one tenant's stream permanently deletes the
        // same-numbered events out of every other tenant's partition.
        builder.AppendConjoinedTenantFilter(session);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
