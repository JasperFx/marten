#nullable enable
using System;
using System.Linq;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Npgsql;
using Polly;

namespace Marten.Util;

internal static class ResilientPipelineBuilderExtensions
{
    public static ResiliencePipelineBuilder AddMartenDefaults(this ResiliencePipelineBuilder builder)
    {
        #region sample_default_polly_setup

        // default Marten policies
        return builder
           .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>()
                    .Handle<MartenCommandException>()
                    .Handle<EventLoaderException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential
            });

        #endregion
    }

    /// <summary>
    /// The retry policy for committing a unit of work. Identical to <see cref="AddMartenDefaults"/>
    /// except that it does not retry when the fate of the previous attempt is unknown. A batch of
    /// document and event operations is not idempotent — replaying it appends the same events a second
    /// time — so it may only be retried when the previous transaction is known to be gone.
    /// </summary>
    public static ResiliencePipelineBuilder AddMartenWriteDefaults(this ResiliencePipelineBuilder builder)
    {
        #region sample_default_write_polly_setup

        // Marten's policy for committing a unit of work
        return builder
            .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>(e => !IsOutcomeInDoubt(e))
                    .Handle<MartenCommandException>(e => !IsOutcomeInDoubt(e))
                    .Handle<EventLoaderException>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential
            });

        #endregion
    }

    /// <summary>
    /// True when we cannot know whether the transaction committed. A command timeout or a dropped
    /// connection kills the connector, so the ROLLBACK never reaches PostgreSQL and the server may
    /// well have committed while the response never made it back to us. An error PostgreSQL itself
    /// reported (<see cref="PostgresException"/>), or a failure raised while post-processing a reader
    /// that Marten then rolled back, leaves no such doubt.
    /// </summary>
    internal static bool IsOutcomeInDoubt(Exception exception)
    {
        return exception switch
        {
            PostgresException => false,
            NpgsqlException => true,
            TimeoutException => true,
            AggregateException aggregate => aggregate.InnerExceptions.Any(IsOutcomeInDoubt),
            { InnerException: { } inner } => IsOutcomeInDoubt(inner),
            _ => false
        };
    }
}
