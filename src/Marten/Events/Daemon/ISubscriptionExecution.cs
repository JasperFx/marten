using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Internals;

namespace Marten.Events.Daemon;

public interface ISubscriptionExecution: IAsyncDisposable
{
    void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent);
    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();
    Task EnsureStorageExists();

    string DatabaseName { get; }
    ShardExecutionMode Mode { get; set; }
}
