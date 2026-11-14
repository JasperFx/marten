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

    // marten#5476: the reverse of ArchiveStream. Queued like it, so nothing happens until SaveChangesAsync,
    // and scoped to the session's tenant under conjoined tenancy. A stream that is not archived, or does not
    // exist, is left alone and is not an error. The identity guard is the one TombstoneStream uses: calling
    // the overload for the other stream identity would otherwise silently match nothing.
    public void UnArchiveStream(Guid streamId)
    {
        var op = _store.Options.EventGraph.EnsureAsGuidStorage(_session).UnArchiveStream(streamId, _session.TenantId);
        _session.QueueOperation(op);
    }

    public void UnArchiveStream(string streamKey)
    {
        var op = _store.Options.EventGraph.EnsureAsStringStorage(_session).UnArchiveStream(streamKey, _session.TenantId);
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
