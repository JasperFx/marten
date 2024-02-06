using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;

namespace Marten.Events.Daemon.New;

public record SubscriptionExecutionRequest(
    long Floor,
    ShardExecutionMode Mode,
    ErrorHandlingOptions ErrorHandling,
    IDaemonRuntime Runtime);

public interface ISubscriptionAgent : IShardAgent
{
    void MarkSuccess(long processedCeiling);
    void MarkHighWater(long sequence);

    Task ReportCriticalFailureAsync(Exception ex);

    long Position { get; }
    AgentStatus Status { get; }

    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();

    Task StartAsync(SubscriptionExecutionRequest request);
}
