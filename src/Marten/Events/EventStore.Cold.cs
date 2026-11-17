#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Internal;

namespace Marten.Events;

internal partial class EventStore
{
    internal void MarkStreamsCold(IReadOnlyList<(string Key, long Version)> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        var events = assertColdStore();
        if (streams.Count == 0) return;

        // Marks queued one after the other share one statement and one lag check.
        if (_session._workTracker is UnitOfWork { LastEventOperation: MarkStreamColdOperation queued }
            && queued.TenantId == _session.TenantId)
        {
            queued.Add(streams);
        }
        else
        {
            var operation = new MarkStreamColdOperation(events, _session.TenantId);
            operation.Add(streams);
            _session.QueueOperation(operation);
        }

        // What this session remembered about these boundaries is about to be stale.
        foreach (var (key, _) in streams)
        {
            _session.ForgetColdVersion(_session.TenantId, key);
        }
    }

    internal ValueTask<long?> ColdVersionOfAsync(StreamAction stream, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(stream);
        assertColdStore();
        return _session.ColdVersionOfAsync(stream, token);
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
