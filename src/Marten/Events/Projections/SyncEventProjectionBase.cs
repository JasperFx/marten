using System.Collections.Generic;

namespace Marten.Events.Projections
{
    public abstract class SyncEventProjectionBase: SyncProjectionBase
    {
        public override void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
                {
                    ApplyEvent(operations, stream, @event);
                }
            }
        }

        public abstract void ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e);
    }
}