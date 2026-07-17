#nullable enable
using System;
using Marten.Events.Archiving;

namespace Marten.Events;

internal partial class EventStore
{
    public void ArchiveStream(Guid streamId)
    {
        var op = _session.EventStorage().ArchiveStream(streamId, _session.TenantId);
        _session.QueueOperation(op);
    }

    public void ArchiveStream(string streamKey)
    {
        var op = _session.EventStorage().ArchiveStream(streamKey, _session.TenantId);
        _session.QueueOperation(op);
    }
}
