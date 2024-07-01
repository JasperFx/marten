using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Internal.Sessions;

namespace Marten.Events;

internal interface IEventAppender
{
    void ProcessEvents(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjectionsValue);

    Task ProcessEventsAsync(EventGraph eventGraph, DocumentSessionBase session,
        IProjection[] inlineProjections, CancellationToken token);
}
