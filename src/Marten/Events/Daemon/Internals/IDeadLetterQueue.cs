using System.Threading.Tasks;

namespace Marten.Events.Daemon.Internals;

public interface IDaemonRuntime
{
    Task RecordDeadLetterEventAsync(DeadLetterEvent @event);
}
