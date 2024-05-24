using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon.Internals;

public interface IDaemonRuntime
{
    Task RecordDeadLetterEventAsync(DeadLetterEvent @event);
    ILogger Logger { get; }
}
