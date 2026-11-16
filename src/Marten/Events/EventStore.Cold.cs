#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;

namespace Marten.Events;

internal partial class EventStore
{
    internal void MarkStreamCold(string streamKey, long throughVersion)
    {
        queueColdBoundary(streamKey, new MarkStreamColdOperation(assertColdStore(), streamKey, throughVersion, _session.TenantId));
    }

    internal ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        assertColdStore();
        return _session.ColdVersionOfAsync(stream, token);
    }

    private void queueColdBoundary(string streamKey, Weasel.Storage.IStorageOperation operation)
    {
        // What this session remembered about the boundary is about to be stale.
        _session.ForgetColdVersion(_session.TenantId, streamKey);
        _session.QueueOperation(operation);
    }

    private EventGraph assertColdStore()
    {
        var events = _store.Options.EventGraph;
        if (events.ColdEventStore == null)
        {
            throw new InvalidOperationException(
                "No cold event store is configured. Use StoreOptions.Events.UseColdEventStore() from Marten.Events.Cold first.");
        }

        return events;
    }
}
