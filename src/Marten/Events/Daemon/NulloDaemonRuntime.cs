using System.Threading.Tasks;

namespace Marten.Events.Daemon;

public class NulloDaemonRuntime: IDaemonRuntime
{
    public Task RecordDeadLetterEventAsync(DeadLetterEvent @event)
    {
        // Nothing, but at least don't blow up
        return Task.CompletedTask;
    }
}
