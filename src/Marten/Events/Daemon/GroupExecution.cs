using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon;

internal record GroupExecution(
    IProjectionSource Source,
    EventRange Range,
    IMartenDatabase Database,
    DocumentStore Store)
{
    public ValueTask<EventRangeGroup> GroupAsync(CancellationToken token)
    {
        return Source.GroupEvents(Store, Database, Range, token);
    }
}