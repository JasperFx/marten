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
/// Both shapes count: Npgsql's client-side CommandTimeout, and a server-side <c>statement_timeout</c>, which
/// arrives as a bare 57014 with no TimeoutException anywhere in the graph.
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

    /// <summary>The client-side shape: CommandTimeout expired and Npgsql gave up on the stream.</summary>
    private static NpgsqlException CommandTimeout()
        => new("Exception while reading from stream", new TimeoutException("Timeout during reading attempt"));

    /// <summary>
    /// The server-side shape: a <c>statement_timeout</c> on the role, database or connection string. PostgreSQL
    /// cancels the query itself, so there is no TimeoutException anywhere in the graph -- only 57014.
    /// </summary>
    private static PostgresException StatementTimeout()
        => new("canceling statement due to statement timeout", "ERROR", "ERROR",
            PostgresErrorCodes.QueryCanceled);

    [Fact]
    public void a_command_timeout_is_not_retried()
        => AttemptsFor(CommandTimeout()).ShouldBe(1);

    [Fact]
    public void a_command_timeout_wrapped_by_marten_is_not_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"), CommandTimeout())).ShouldBe(1);

    [Fact]
    public void a_server_side_statement_timeout_is_not_retried()
        => AttemptsFor(StatementTimeout()).ShouldBe(1);

    [Fact]
    public void a_server_side_statement_timeout_wrapped_by_marten_is_not_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"), StatementTimeout())).ShouldBe(1);

    /// <summary>
    /// IsTimeout walks the same graph WriteRetryClassifier does, so a timeout folded into an AggregateException
    /// -- the shape ExecuteBatchPagesAsync produces for per-operation failures -- is still seen.
    /// </summary>
    [Fact]
    public void a_timeout_inside_an_aggregate_is_not_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"),
            new AggregateException(new Exception("unrelated"), CommandTimeout()))).ShouldBe(1);

    [Fact]
    public void other_npgsql_failures_are_still_retried()
    {
        AttemptsFor(new NpgsqlException("no inner")).ShouldBe(4);
        AttemptsFor(new PostgresException("boom", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected))
            .ShouldBe(4);
    }

    /// <summary>
    /// 57014 is the only class-57 state treated as a timeout. The rest of the class is a dead or dying
    /// connection, where a retry gets a fresh connection from AutoClosingLifetime and may well succeed.
    /// </summary>
    [Fact]
    public void other_class_57_failures_are_still_retried()
    {
        AttemptsFor(new PostgresException("shutting down", "FATAL", "FATAL", PostgresErrorCodes.AdminShutdown))
            .ShouldBe(4);
        AttemptsFor(new PostgresException("terminating", "FATAL", "FATAL", PostgresErrorCodes.CrashShutdown))
            .ShouldBe(4);
    }

    [Fact]
    public void other_failures_wrapped_by_marten_are_still_retried()
        => AttemptsFor(new MartenCommandException(new NpgsqlCommand("select 1"), new NpgsqlException("no inner")))
            .ShouldBe(4);
}
