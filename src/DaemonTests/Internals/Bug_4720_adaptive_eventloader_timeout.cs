using System;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

public class Bug_4720_adaptive_eventloader_timeout
{
    // #4720: the adaptive EventLoader's timeout classifier only inspected the OUTERMOST exception,
    // but AutoClosingLifetime re-throws every command failure through MartenExceptionTransformer,
    // which wraps the original NpgsqlException/PostgresException into a MartenCommandException
    // (NOT an NpgsqlException). The classifier therefore returned false for every real timeout and
    // the skip-ahead / window-step fallback never engaged. It must now walk the inner-exception chain.

    private static PostgresException StatementTimeout()
        => new("canceling statement due to statement timeout", "ERROR", "ERROR",
            PostgresErrorCodes.QueryCanceled); // 57014

    [Fact]
    public void wrapped_statement_timeout_is_recognized()
    {
        // This is the exact shape #4720 reported: the 57014 PostgresException buried inside a
        // MartenCommandException. The pre-fix code returned false here.
        var wrapped = new MartenCommandException((NpgsqlCommand)null, StatementTimeout());

        EventLoader.isTimeoutException(wrapped).ShouldBeTrue();
    }

    [Fact]
    public void wrapped_client_timeout_is_recognized()
    {
        var clientTimeout = new NpgsqlException("Exception while reading from stream", new TimeoutException());
        var wrapped = new MartenCommandException((NpgsqlCommand)null, clientTimeout);

        EventLoader.isTimeoutException(wrapped).ShouldBeTrue();
    }

    [Fact]
    public void deeply_nested_timeout_is_recognized()
    {
        var nested = new InvalidOperationException("outer",
            new AggregateException(new MartenCommandException((NpgsqlCommand)null, StatementTimeout())));

        EventLoader.isTimeoutException(nested).ShouldBeTrue();
    }

    [Fact]
    public void bare_statement_timeout_is_still_recognized()
    {
        EventLoader.isTimeoutException(StatementTimeout()).ShouldBeTrue();
    }

    [Fact]
    public void unrelated_exception_is_not_a_timeout()
    {
        EventLoader.isTimeoutException(new InvalidOperationException("boom")).ShouldBeFalse();
        EventLoader.isTimeoutException(
            new MartenCommandException((NpgsqlCommand)null, new InvalidOperationException("boom")))
            .ShouldBeFalse();
    }
}
