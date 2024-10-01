using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten.Events.Daemon.Internals;

public class NulloDaemonRuntime: IDaemonRuntime
{
    public Task RecordDeadLetterEventAsync(DeadLetterEvent @event)
    {
        // Nothing, but at least don't blow up
        return Task.CompletedTask;
    }

    public ILogger Logger { get; } = NullLogger.Instance;

    public long CurrentHighWaterMark { get; set; }

    public long HighWaterMark()
    {
        return CurrentHighWaterMark;
    }
}
