using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionAgent : IShardAgent
{
    void MarkSuccess(long processedCeiling);
    void MarkHighWater(long sequence);

    long Position { get; }

    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();

    void Start(long floor, ShardExecutionMode mode);
}
