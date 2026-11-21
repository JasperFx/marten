#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Polly;
using Polly.Retry;

namespace Marten;

/// <summary>
///     Marten's default Polly policies that you can build on in your own pipelines.
/// </summary>
public static class MartenResiliencePipelineBuilderExtensions
{
    /// <summary>
    ///     How many times <see cref="AddMartenDaemonBatchDefaults" /> builds a batch again before the shard pauses.
    /// </summary>
    internal const int DaemonBatchRetryAttempts = 3;

    /// <summary>
    ///     Add Marten's default retry policy for the async daemon building a projection batch, the pipeline
    ///     <see cref="StoreOptions.ConfigureDaemonPolly" /> replaces. Use it there to start from the default and add
    ///     to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It retries a failure JasperFx judges transient (a dropped connection, a deadlock, a command timeout or
    ///         an exhausted connection pool), but not a database error that would fail the same way again, like a
    ///         missing column or table. It retries up
    ///         to 3 times, so a batch is built at most 4 times, starting at 250ms and doubling, with jitter. When the
    ///         retries run out, the shard pauses with <c>ShardFailure.IsTransient</c> true.
    ///     </para>
    ///     <para>
    ///         Unlike the session pipeline, this one retries timeouts. Every command in the batch build already runs
    ///         through the session's own pipeline, which retries the other failures, so the count here stays small
    ///         to keep the two from multiplying into a long stall (#5587).
    ///     </para>
    /// </remarks>
    public static ResiliencePipelineBuilder AddMartenDaemonBatchDefaults(this ResiliencePipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        #region sample_default_daemon_batch_polly_setup

        // Marten's policy for building an async projection batch again after a transient failure:
        // a dropped connection, a deadlock, a command timeout or an exhausted connection pool
        return builder
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is { } e && TransientFailures.IsTransient(e) &&
                    !ProjectionExceptions.IsSystemicDatabaseFailure(e)),
                MaxRetryAttempts = DaemonBatchRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(250),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            });

        #endregion
    }
}
