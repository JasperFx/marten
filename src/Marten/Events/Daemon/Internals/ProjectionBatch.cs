#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Parking;
using Marten.Events.Operations;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Sessions;
using Marten.Services;

using Marten.Internal;

namespace Marten.Events.Daemon.Internals;

internal class ProjectionBatch: IProjectionBatch<IDocumentOperations, IQuerySession>
{
    private readonly DocumentSessionBase _session;
    private readonly ProjectionUpdateBatch _batch;
    private readonly ShardExecutionMode _mode;
    private readonly IEventStorage _eventStorage;

    public ProjectionBatch(DocumentSessionBase session, ProjectionUpdateBatch batch, ShardExecutionMode mode)
    {
        _session = session;
        _batch = batch;
        _mode = mode;
        _eventStorage = session.EventStorage();
    }

    public async ValueTask RecordProgress(EventRange range)
    {
        if (range.SequenceFloor == 0)
        {
            await _batch.Queue.PostAsync(new InsertProjectionProgress(((IMartenSession)_session).Options.EventGraph, range)).ConfigureAwait(false);
        }
        else
        {
            await _batch.Queue.PostAsync(new UpdateProjectionProgress(((IMartenSession)_session).Options.EventGraph, range)).ConfigureAwait(false);
        }
    }

    // marten#6003: the parked row and the slice index rows ride the batch, so they commit with its progression

    public void ParkIdentity(ParkedIdentity parked)
    {
        ArgumentNullException.ThrowIfNull(parked);
        _batch.Queue.Post(new ParkIdentityOperation(((IMartenSession)_session).Options.EventGraph, parked));
    }

    public void UnparkIdentity(ParkedIdentityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _batch.Queue.Post(new UnparkIdentityOperation(((IMartenSession)_session).Options.EventGraph, key));
    }

    public void AppendSliceIndex(SliceIndexRows rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Entries.Count == 0) return;

        _batch.Queue.Post(new AppendSliceIndexOperation(((IMartenSession)_session).Options.EventGraph, rows));
    }

    public async Task ExecuteAsync(CancellationToken token)
    {
        await _batch.WaitForCompletion().ConfigureAwait(false);
        await _session.ExecuteBatchAsync(_batch, token).ConfigureAwait(true);
    }

    public void QuickAppendEventWithVersion(StreamAction action, IEvent @event)
    {
        var op = _eventStorage.QuickAppendEventWithVersion(action, @event);
        _batch.Queue.Post(op);
    }

    public void UpdateStreamVersion(StreamAction action)
    {
        var op = _eventStorage.UpdateStreamVersion(action);
        _batch.Queue.Post(op);
    }

    public void QuickAppendEvents(StreamAction action)
    {
        var op = _eventStorage.QuickAppendEvents(action);

        // marten#6003: the quick append writes every registered tag type's column, and reads them from the event
        // graph, which QuickEventAppender sets and this path didn't. With any DCB tag type registered, a side
        // effect that appended events from an async projection failed the batch with a NullReferenceException.
        if (op is QuickAppendEventsOperationBase quickAppend)
        {
            quickAppend.Events ??= ((IMartenSession)_session).Options.EventGraph;
        }

        _batch.Queue.Post(op);
    }

    public async Task PublishMessageAsync(object message, string tenantId)
    {
        var batch = await _batch.CurrentMessageBatch(_session).ConfigureAwait(false);

        // TODO -- need to pass through the tenant id
        await batch.PublishAsync(message, tenantId).ConfigureAwait(false);
    }

    /// <summary>
    ///     Metadata-aware overload matching
    ///     <see cref="IProjectionBatch.PublishMessageAsync(object, MessageMetadata)"/>.
    ///     Forwards to the underlying <see cref="IMessageSink"/> implementation with
    ///     full metadata so downstream consumers (e.g. Wolverine's
    ///     MartenToWolverineMessageBatch) can map it to their native delivery
    ///     options (correlation id, causation id, headers, user name).
    /// </summary>
    public async Task PublishMessageAsync(object message, MessageMetadata metadata)
    {
        var batch = await _batch.CurrentMessageBatch(_session).ConfigureAwait(false);
        await batch.PublishAsync(message, metadata).ConfigureAwait(false);
    }

    public IDocumentOperations SessionForTenant(string tenantId)
    {
        if (tenantId.IsEmpty() || tenantId == StorageConstants.DefaultTenantId)
        {
            var sessionOptions = SessionOptions.ForDatabase(_session.Database);
            sessionOptions.Tracking = _session.TrackingMode;

            return new ProjectionDocumentSession((DocumentStore)_session.DocumentStore, _batch,
                sessionOptions, _mode);
        }

        var forDatabase = SessionOptions.ForDatabase(tenantId, _session.Database);
        forDatabase.Tracking = _session.TrackingMode;

        return new ProjectionDocumentSession((DocumentStore)_session.DocumentStore, _batch,
            forDatabase, _mode);
    }

    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>(string tenantId) where TId : notnull where TDoc : notnull
    {
        var session = SessionForTenant(tenantId);
        var storage = _session.StorageFor<TDoc, TId>();
        return new ProjectionStorage<TDoc, TId>((DocumentSessionBase)session, storage);
    }

    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>() where TDoc : notnull where TId : notnull
    {
        var storage = _session.StorageFor<TDoc, TId>();
        return new ProjectionStorage<TDoc, TId>(_session, storage);
    }

    private bool _wasDisposed;

    public async ValueTask DisposeAsync()
    {
        if (_wasDisposed) return;

        await _session.DisposeAsync().ConfigureAwait(false);
        await _batch.DisposeAsync().ConfigureAwait(false);

        _wasDisposed = true;
    }
}
