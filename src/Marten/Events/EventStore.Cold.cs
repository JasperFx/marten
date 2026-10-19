#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;

namespace Marten.Events;

internal partial class EventStore
{
    public void MarkStreamCold(Guid streamId, long throughVersion)
    {
        queueColdBoundary(streamId, new MarkStreamColdOperation(assertColdStore(), streamId, throughVersion, _session.TenantId));
    }

    public void MarkStreamCold(string streamKey, long throughVersion)
    {
        queueColdBoundary(streamKey, new MarkStreamColdOperation(assertColdStore(), streamKey, throughVersion, _session.TenantId));
    }

    public void MarkStreamWarm(Guid streamId)
    {
        queueColdBoundary(streamId, new MarkStreamWarmOperation(assertColdStore(), streamId, _session.TenantId));
    }

    public void MarkStreamWarm(string streamKey)
    {
        queueColdBoundary(streamKey, new MarkStreamWarmOperation(assertColdStore(), streamKey, _session.TenantId));
    }

    public ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        assertColdStore();
        return _session.ColdVersionOfAsync(stream, token);
    }

    private void queueColdBoundary(object streamId, Weasel.Storage.IStorageOperation operation)
    {
        // What this session remembered about the boundary is about to be stale.
        _session.ForgetColdVersion(_session.TenantId, streamId);
        _session.QueueOperation(operation);
    }

    private EventGraph assertColdStore()
    {
        var events = _store.Options.EventGraph;
        if (events.ColdEventStore == null)
        {
            throw new InvalidOperationException(
                "No cold event store is configured. Use StoreOptions.Events.UseColdEventStore() first.");
        }

        return events;
    }
}
