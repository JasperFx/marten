#nullable enable
using System;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Npgsql;
using Polly;

namespace Marten.Util;

internal static class ResilientPipelineBuilderExtensions
{
    public static ResiliencePipelineBuilder AddMartenDefaults(this ResiliencePipelineBuilder builder)
    {
        #region sample_default_Polly_setup

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
}
