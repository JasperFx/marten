using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionExecution: IAsyncDisposable
{
    void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent);
    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();
}
