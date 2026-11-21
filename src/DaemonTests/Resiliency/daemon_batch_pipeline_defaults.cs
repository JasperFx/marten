using System;
using System.Threading.Tasks;
using Marten;
using Npgsql;
using Polly;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

/// <summary>
/// The daemon's default batch pipeline is public as <see cref="MartenResiliencePipelineBuilderExtensions.AddMartenDaemonBatchDefaults" />,
/// so a <see cref="StoreOptions.ConfigureDaemonPolly" /> can start from it and add to it. These run the pipeline
/// directly, without a daemon or a database. <see cref="transient_batch_build_failures" /> covers the daemon using it.
/// </summary>
public class daemon_batch_pipeline_defaults
{
    private static Exception poolTimeout() =>
        new NpgsqlException("The connection pool has been exhausted", new TimeoutException());

    private static async Task<(int Attempts, Exception Thrown)> runAsync(ResiliencePipeline pipeline,
        Func<Exception> failure)
    {
        var attempts = 0;
        var thrown = await Should.ThrowAsync<Exception>(async () =>
            await pipeline.ExecuteAsync(_ =>
            {
                attempts++;
                throw failure();
            }, TestContext.Current.CancellationToken));

        return (attempts, thrown);
    }

    [Fact]
    public void the_default_is_public_api()
    {
        typeof(MartenResiliencePipelineBuilderExtensions).IsPublic.ShouldBeTrue();
    }

    [Fact]
    public async Task the_default_retries_a_transient_failure_3_times_then_gives_it_back()
    {
        var pipeline = new ResiliencePipelineBuilder().AddMartenDaemonBatchDefaults().Build();

        var (attempts, thrown) = await runAsync(pipeline, poolTimeout);

        // The first build, then the 3 retries the XML docs and retries.md promise
        attempts.ShouldBe(4);
        MartenResiliencePipelineBuilderExtensions.DaemonBatchRetryAttempts.ShouldBe(3);
        thrown.ShouldBeOfType<NpgsqlException>();
    }

    [Fact]
    public async Task the_default_does_not_retry_a_failure_that_is_not_transient()
    {
        var pipeline = new ResiliencePipelineBuilder().AddMartenDaemonBatchDefaults().Build();

        var (attempts, thrown) = await runAsync(pipeline, () => new InvalidOperationException("Not transient"));

        attempts.ShouldBe(1);
        thrown.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task configure_daemon_polly_can_start_from_the_default_and_add_to_it()
    {
        var opts = new StoreOptions();

        #region sample_configure_daemon_polly_from_the_default

        // Keep Marten's default retries, and when they run out, wait 5 seconds
        // and run them once more before the shard pauses
        opts.ConfigureDaemonPolly(builder => builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromSeconds(5)
            })
            .AddMartenDaemonBatchDefaults());

        #endregion

        var (attempts, thrown) = await runAsync(opts.DaemonResiliencePipeline, poolTimeout);

        // Two rounds of Marten's first build plus 3 retries
        attempts.ShouldBe(8);
        thrown.ShouldBeOfType<NpgsqlException>();
    }
}
