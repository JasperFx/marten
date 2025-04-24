#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
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

    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>(string tenantId)
    {
        var session = SessionForTenant(tenantId);
        var storage = _session.StorageFor<TDoc, TId>();
        return new ProjectionStorage<TDoc, TId>((DocumentSessionBase)session, storage);
    }

    public IProjectionStorage<TDoc, TId> ProjectionStorageFor<TDoc, TId>()
    {
        var storage = _session.StorageFor<TDoc, TId>();
        return new ProjectionStorage<TDoc, TId>(_session, storage);
    }

    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync().ConfigureAwait(false);
        await _batch.DisposeAsync().ConfigureAwait(false);
    }
}
