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

    // marten#5673: the hard delete. Queued, so nothing happens until SaveChangesAsync, and scoped to
    // the session's tenant under conjoined tenancy. A missing stream deletes nothing and is not an error.
    public void TombstoneStream(Guid streamId)
    {
        var op = _store.Options.EventGraph.EnsureAsGuidStorage(_session).DeleteStream(streamId, _session.TenantId);
        _session.QueueOperation(op);
    }

    public void TombstoneStream(string streamKey)
    {
        var op = _store.Options.EventGraph.EnsureAsStringStorage(_session).DeleteStream(streamKey, _session.TenantId);
        _session.QueueOperation(op);
    }
}
