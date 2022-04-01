using System;
using Marten.Events.Archiving;

#nullable enable
namespace Marten.Events
{
    internal partial class EventStore
    {
        public void ArchiveStream(Guid streamId)
        {
            var op = new ArchiveStreamOperation(_store.Events, streamId);
            _session.QueueOperation(op);
        }

        public void ArchiveStream(string streamKey)
        {
            var op = new ArchiveStreamOperation(_store.Events, streamKey);
            _session.QueueOperation(op);
        }

    }
}
