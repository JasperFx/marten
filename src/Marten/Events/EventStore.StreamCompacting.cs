using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using Marten.Events.Protected;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Postgresql;

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
    {
        var request = new StreamCompactingRequest<T>(streamKey);
        configure?.Invoke(request);

        await request.ExecuteAsync(_session).ConfigureAwait(false);
    }

    public async Task CompactStreamAsync<T>(Guid streamId, Action<StreamCompactingRequest<T>>? configure = null)
    {
        var request = new StreamCompactingRequest<T>(streamId);
        configure?.Invoke(request);

        await request.ExecuteAsync(_session).ConfigureAwait(false);
    }
}

public class StreamCompactingRequest<T>
{
    public StreamCompactingRequest(string? streamKey)
    {
        StreamKey = streamKey;
    }

    public StreamCompactingRequest(Guid? streamId)
    {
        StreamId = streamId;
    }

    /// <summary>
    /// The identity of the stream if using string identified streams
    /// </summary>
    public string StreamKey { get; private set; }

    /// <summary>
    /// The identity of the stream if using Guid identified streams
    /// </summary>
    public Guid? StreamId { get; private set; }

    /// <summary>
    /// If specified, the version at which the stream is going to be compacted. Default 0 means
    /// the latest
    /// </summary>
    public long Version { get; set; } = 0;

    /// <summary>
    /// If specified, this operation will compact the events below the timestamp
    /// </summary>
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Optional mechanism to carry out an archiving step for the events before the
    /// compacting operation is completed and these events are permanently deleted
    /// </summary>
    public IEventsArchiver? Archiver { get; set; }

    /// <summary>
    /// CancellationToken for just this operation. Default is None
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    internal async Task ExecuteAsync(DocumentSessionBase session)
    {
        var logger = session.Options.LogFactory?.CreateLogger<StreamCompactingRequest<T>>() ??
                     session.Options.DotNetLogger ?? NullLogger<StreamCompactingRequest<T>>.Instance;

        // 1. Find the aggregator
        var aggregator = findAggregator(session);

        // 2. Find the events
        IReadOnlyList<IEvent> events = null;
        if (session.Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            events = await session.Events.FetchStreamAsync(StreamId.Value, Version, Timestamp, token:CancellationToken).ConfigureAwait(false);
        }
        else
        {
            events = await session.Events.FetchStreamAsync(StreamKey, Version, Timestamp, token:CancellationToken).ConfigureAwait(false);
        }

        if (!events.Any()) return;
        if (events is [{ Data: Compacted<T> }]) return;

        var sequences = events.Select(x => x.Sequence).Take(events.Count - 1).ToArray();

        Version = events.Last().Version;
        Sequence = events.Last().Sequence;

        // 3. Aggregate up to the new snapshot
        var aggregate = await aggregator.BuildAsync(events, session, default, CancellationToken).ConfigureAwait(false);

        if (Archiver != null)
        {
            await Archiver.MaybeArchiveAsync(session, this, events, CancellationToken).ConfigureAwait(false);
        }

        if (session.Options.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            logger.LogInformation("Successfully archived events for Stream {Id} from Version {Version} and down", StreamId, Version);
        }
        else
        {
            logger.LogInformation("Successfully archived events for Stream {Key} from Version {Version} and down", StreamKey, Version);
        }

        var compacted = new Compacted<T>(aggregate, StreamId.HasValue ? StreamId.Value : Guid.Empty, StreamKey);

        session.Events.CompletelyReplaceEvent(Sequence, compacted);

        session.QueueOperation(new DeleteEventsOperation(sequences));
    }

    /// <summary>
    /// The event sequence of the last event being compacted and maybe archived
    /// </summary>
    public long Sequence { get; private set; }

    private static IAggregator<T, IQuerySession> findAggregator(DocumentSessionBase session)
    {
        if (!session.Options.Projections.TryFindAggregate(typeof(T), out var projection))
        {
            throw new InvalidOperationException("Unable to find an Aggregation Projection for type " +
                                                typeof(T).FullNameInCode());
        }

        var aggregator = projection as IAggregator<T, IQuerySession>;
        if (aggregator == null)
        {
            throw new InvalidOperationException(
                $"Type {projection.GetType().FullNameInCode()} does not implement interface {typeof(IAggregator<T, IDocumentOperations>).FullNameInCode()}");
        }

        return aggregator;
    }
}

/// <summary>
/// Callback interface for executing
/// </summary>
public interface IEventsArchiver
{
    Task MaybeArchiveAsync<T>(IDocumentOperations operations, StreamCompactingRequest<T> request, IReadOnlyList<IEvent> events,
        CancellationToken cancellation);
}

internal class DeleteEventsOperation: IStorageOperation
{
    private readonly long[] _sequences;

    public DeleteEventsOperation(long[] sequences)
    {
        _sequences = sequences;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append($"delete from {session.Options.Events.DatabaseSchemaName}.mt_events where seq_id = ANY(");
        builder.AppendParameter(_sequences);
        builder.Append(")");
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}

