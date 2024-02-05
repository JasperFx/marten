using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionAgent : IShardAgent
{
    void MarkSuccess(long processedCeiling);
    void MarkHighWater(long sequence);

    long Position { get; }
    AgentStatus Status { get; }

    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();

    Task StartAsync(long floor, ShardExecutionMode mode);
}
