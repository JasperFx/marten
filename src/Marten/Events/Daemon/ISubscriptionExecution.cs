using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Internals;
using Marten.Storage;

namespace Marten.Events.Daemon;

public interface ISubscriptionExecution: IAsyncDisposable
{
    void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent);
    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();
    Task EnsureStorageExists();

    string DatabaseName { get; }
    ShardExecutionMode Mode { get; set; }

    bool TryBuildReplayExecutor(out IReplayExecutor executor);
}

/// <summary>
/// Use to create an optimized projection or subscription replay in the case of rewinding all the way
/// back to sequence = 0 (projection rebuilds most likely)
/// </summary>
public interface IReplayExecutor
{
    Task StartAsync(SubscriptionExecutionRequest request,
        ISubscriptionController controller, CancellationToken cancellation);
}
