using System.Threading.Tasks;

namespace Marten.Events.Daemon;

public interface IDaemonRuntime
{
    Task RecordDeadLetterEventAsync(DeadLetterEvent @event);
}
