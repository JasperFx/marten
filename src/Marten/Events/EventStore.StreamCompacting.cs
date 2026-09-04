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

        var request = new StreamCompactingRequest<T>(streamKey);
        configure?.Invoke(request);

        await request.ExecuteAsync(_session).ConfigureAwait(false);
    }

    public async Task CompactStreamAsync<T>(Guid streamId, Action<StreamCompactingRequest<T>>? configure = null)
        where T : class
    {
        // The mirror case is worse than a bad message: on a string-identified store ExecuteAsync
        // takes the AsString branch, reads a null StreamKey, matches no stream and returns at the
        // `if (!events.Any()) return;` guard -- so compaction silently did nothing at all.
        _store.Events.EnsureAsGuidStorage(_session);

        var request = new StreamCompactingRequest<T>(streamId);
        configure?.Invoke(request);

        await request.ExecuteAsync(_session).ConfigureAwait(false);
    }
}

/// <summary>
///     Marten-side execution of <see cref="StreamCompactingRequest{T}"/> (the data
///     shape now lives in <see cref="JasperFx.Events.Protected"/> per the dedupe
///     pillar — see jasperfx#214 / PR #274). Each product owns its own
///     <c>ExecuteAsync</c>; only the request shape and the
///     <see cref="IEventsArchiver{TOperations}"/> hook are shared.
/// </summary>
internal static class StreamCompactingExecution
{
    /// <summary>
    ///     Drive the compaction against a Marten <see cref="DocumentSessionBase"/>:
    ///     fetch the events to be replaced, build a <see cref="Compacted{T}"/>
    ///     snapshot via the aggregator, optionally invoke the archiver (if the
    ///     request's <see cref="StreamCompactingRequest{T}.Archiver"/> closes the
    ///     generic over Marten's <see cref="IDocumentOperations"/>), then queue the
    ///     replacement + delete operations on the session.
    /// </summary>
    internal static async Task ExecuteAsync<T>(this StreamCompactingRequest<T> request, DocumentSessionBase session)
        where T : class
    {
        var logger = ((IMartenSession)session).Options.LogFactory?.CreateLogger<StreamCompactingRequest<T>>() ??
                     ((IMartenSession)session).Options.DotNetLogger ?? NullLogger<StreamCompactingRequest<T>>.Instance;

        // 1. Find the aggregator
        var aggregator = FindAggregator<T>(session);

        // 2. Find the events
        IReadOnlyList<IEvent> events;
        if (((IMartenSession)session).Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            events = await session.Events.FetchStreamAsync(request.StreamId!.Value, request.Version,
                request.Timestamp, token: request.CancellationToken).ConfigureAwait(false);
        }
        else
        {
            events = await session.Events.FetchStreamAsync(request.StreamKey!, request.Version,
                request.Timestamp, token: request.CancellationToken).ConfigureAwait(false);
        }

        if (!events.Any()) return;
        if (events is [{ Data: Compacted<T> }]) return;

        var sequences = events.Select(x => x.Sequence).Take(events.Count - 1).ToArray();

        request.Version = events[events.Count - 1].Version;
        request.Sequence = events[events.Count - 1].Sequence;

        // 3. Aggregate up to the new snapshot
        var aggregate = await aggregator.BuildAsync(events, session, default, request.CancellationToken)
            .ConfigureAwait(false);

        // 4. Optional archiving. The lifted IEventsArchiver marker is non-generic
        //    so the data class doesn't have to flow a TOperations parameter; the
        //    product downcasts to the closed-generic at execution time. Marten's
        //    callbacks close on IDocumentOperations.
        if (request.Archiver is IEventsArchiver<IDocumentOperations> archiver)
        {
            await archiver.MaybeArchiveAsync(session, request, events, request.CancellationToken)
                .ConfigureAwait(false);
        }

        if (((IMartenSession)session).Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            logger.LogInformation("Successfully archived events for Stream {Id} from Version {Version} and down",
                request.StreamId, request.Version);
        }
        else
        {
            logger.LogInformation("Successfully archived events for Stream {Key} from Version {Version} and down",
                request.StreamKey, request.Version);
        }

        var compacted = new Compacted<T>(aggregate!,
            request.StreamId ?? Guid.Empty, request.StreamKey ?? string.Empty);

        session.Events.CompletelyReplaceEvent(request.Sequence, compacted);

        session.QueueOperation(new DeleteEventsOperation(sequences));

        // jasperfx#740 (marten#5333): record the compaction watermark on mt_streams so
        // StreamState.CompactedVersion — and the (Version - CompactedVersion) growth measure the
        // Stream Compaction Policies are built on — reads truthfully. request.Version is the
        // version of the last event folded into the Compacted<T> snapshot: the caller's cutoff for
        // a partial compaction, the stream's current version for a full one.
        session.QueueOperation(new RecordCompactionWatermarkOperation(
            ((IMartenSession)session).Options.EventGraph, request.StreamId, request.StreamKey, request.Version));
    }

    private static IAggregator<T, IQuerySession> FindAggregator<T>(DocumentSessionBase session) where T : class
    {
        if (!((IMartenSession)session).Options.Projections.TryFindAggregate(typeof(T), out var projection))
        {
            throw new InvalidOperationException("Unable to find an Aggregation Projection for type " +
                                                typeof(T).FullNameInCode());
        }

        var aggregator = projection as IAggregator<T, IQuerySession>;
        if (aggregator == null)
        {
            throw new InvalidOperationException(
                $"Type {projection!.GetType().FullNameInCode()} does not implement interface " +
                $"{typeof(IAggregator<T, IDocumentOperations>).FullNameInCode()}");
        }

        return aggregator;
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
/// archivers now close the generic over <see cref="IDocumentOperations"/>; Polecat
/// archivers close it over <c>Polecat.IDocumentOperations</c>.
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
        builder.Append($"delete from {((IMartenSession)session).Options.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(");
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
