#nullable enable
using System;
using Marten.Exceptions;
using Marten.Util;
using Npgsql;
using Polly;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// Pins the default read pipeline. A command that timed out has already spent the whole CommandTimeout plus
/// Npgsql's cancel request, so retrying it three times holds the caller for four timeouts instead of one.
/// </summary>
public class read_retry_classification
{
    private static int AttemptsFor(Exception exception)
    {
        var pipeline = new ResiliencePipelineBuilder().AddMartenDefaults().Build();
        var attempts = 0;

        Should.Throw(() => pipeline.Execute(() =>
        {
            attempts++;
            throw exception;
        }), exception.GetType());

        return attempts;
    }

    private static NpgsqlException CommandTimeout()
        => new("Exception while reading from stream", new TimeoutException("Timeout during reading attempt"));

    [Fact]
    public void a_command_timeout_is_not_retried()
        => AttemptsFor(CommandTimeout()).ShouldBe(1);

    [Fact]
    public void a_command_timeout_wrapped_by_marten_is_not_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"), CommandTimeout())).ShouldBe(1);

    [Fact]
    public void other_npgsql_failures_are_still_retried()
    {
        AttemptsFor(new NpgsqlException("no inner")).ShouldBe(4);
        AttemptsFor(new PostgresException("boom", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected))
            .ShouldBe(4);
    }

    [Fact]
    public void other_failures_wrapped_by_marten_are_still_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"), new NpgsqlException("no inner")))
            .ShouldBe(4);
}
