using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.Internals;

public interface IEventLoader
{
    Task<EventPage> LoadAsync(EventRequest request, CancellationToken token);
}
