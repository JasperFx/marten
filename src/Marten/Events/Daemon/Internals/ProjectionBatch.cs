#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Sessions;
using Marten.Services;

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
            await _batch.Queue.PostAsync(new InsertProjectionProgress(_session.Options.EventGraph, range)).ConfigureAwait(false);
        }
        else
        {
            await _batch.Queue.PostAsync(new UpdateProjectionProgress(_session.Options.EventGraph, range)).ConfigureAwait(false);
        }
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
        _batch.Queue.Post(op);
    }

    public async Task PublishMessageAsync(object message, string tenantId)
    {
        var batch = await _batch.CurrentMessageBatch(_session).ConfigureAwait(false);

        // TODO -- need to pass through the tenant id
        await batch.PublishAsync(message, tenantId).ConfigureAwait(false);
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
