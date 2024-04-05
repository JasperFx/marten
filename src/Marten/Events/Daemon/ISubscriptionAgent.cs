using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;

namespace Marten.Events.Daemon;

public record SubscriptionExecutionRequest(
    long Floor,
    ShardExecutionMode Mode,
    ErrorHandlingOptions ErrorHandling,
    IDaemonRuntime Runtime);

/// <summary>
///     Used internally by asynchronous projections.
/// </summary>
// This is public because it's used by the generated code
public interface ISubscriptionAgent
{
    ShardName Name { get; }
    ShardExecutionMode Mode { get; }
    void MarkSuccess(long processedCeiling);
    void MarkHighWater(long sequence);

    /// <summary>
    /// The current error handling configuration for this projection or subscription
    /// </summary>
    ErrorHandlingOptions ErrorOptions { get; }

    /// <summary>
    /// Tell the governing subscription agent that there was a critical error that
    /// should pause the subscription or projection
    /// </summary>
    /// <param name="ex"></param>
    /// <returns></returns>
    Task ReportCriticalFailureAsync(Exception ex);

    long Position { get; }
    AgentStatus Status { get; }

    Task StopAndDrainAsync(CancellationToken token);
    Task HardStopAsync();

    Task StartAsync(SubscriptionExecutionRequest request);

    /// <summary>
    /// Record a dead letter event for the failure to process the current
    /// event
    /// </summary>
    /// <param name="event"></param>
    /// <returns></returns>
    Task RecordDeadLetterEventAsync(DeadLetterEvent @event);

    DateTimeOffset? PausedTime { get; }
    Task ReplayAsync(SubscriptionExecutionRequest request, long highWaterMark, TimeSpan timeout);
}
