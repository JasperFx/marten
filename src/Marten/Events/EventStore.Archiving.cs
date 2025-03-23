#nullable enable
using System;
using Marten.Events.Archiving;

namespace Marten.Events;

internal partial class EventStore
{
    public void ArchiveStream(Guid streamId)
    {
        var op = new ArchiveStreamOperation(_store.Events, streamId){TenantId = _session.TenantId};
        _session.QueueOperation(op);
    }

    public void ArchiveStream(string streamKey)
    {
        var op = new ArchiveStreamOperation(_store.Events, streamKey){TenantId = _session.TenantId};
        _session.QueueOperation(op);
    }
}
