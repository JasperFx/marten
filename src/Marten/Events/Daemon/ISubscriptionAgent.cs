using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;

namespace Marten.Events.Daemon;

public record SubscriptionExecutionRequest(
    long Floor,
    ShardExecutionMode Mode,
    ErrorHandlingOptions ErrorHandling,
    IDaemonRuntime Runtime);

public interface ISubscriptionController
{
    ShardExecutionMode Mode { get; }

    /// <summary>
    /// The current error handling configuration for this projection or subscription
    /// </summary>
    ErrorHandlingOptions ErrorOptions { get; }

    ShardName Name { get; }
    AsyncOptions Options { get; }

    void MarkSuccess(long processedCeiling);

    /// <summary>
    /// Tell the governing subscription agent that there was a critical error that
    /// should pause the subscription or projection
    /// </summary>
    /// <param name="ex"></param>
    /// <returns></returns>
    Task ReportCriticalFailureAsync(Exception ex);

    /// <summary>
    /// Tell the governing subscription agent that there was a critical error that
    /// should pause the subscription or projection
    /// </summary>
    /// <param name="ex"></param>
    /// <param name="lastProcessed">This allows a subscription to stop at a point within a batch of events</param>
    /// <returns></returns>
    Task ReportCriticalFailureAsync(Exception ex, long lastProcessed);

    /// <summary>
    /// Record a dead letter event for the failure to process the current event
    /// </summary>
    /// <param name="event"></param>
    /// <param name="ex"></param>
    /// <returns></returns>
    Task RecordDeadLetterEventAsync(IEvent @event, Exception ex);
}

/// <summary>
///     Used internally by asynchronous projections.
/// </summary>
// This is public because it's used by the generated code
public interface ISubscriptionAgent: ISubscriptionController
{
    void MarkHighWater(long sequence);

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
    ISubscriptionMetrics Metrics { get; }
    Task ReplayAsync(SubscriptionExecutionRequest request, long highWaterMark, TimeSpan timeout);
}
