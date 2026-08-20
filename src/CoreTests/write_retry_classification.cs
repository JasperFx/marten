#nullable enable
using System;
using System.IO;
using System.Net.Sockets;
using Marten.Exceptions;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5262: pins the classification behind <see cref="StoreOptions.WriteResiliencePipeline" />. A commit is one
/// transaction carrying document writes and event appends, and appends are not idempotent — so the question is
/// not "was this transient" but "do we know the previous attempt left nothing behind".
/// </summary>
public class write_retry_classification
{
    private static PostgresException Postgres(string sqlState)
        => new("boom", "ERROR", "ERROR", sqlState);

    public static TheoryData<string> RetryableStates => new()
    {
        // Class 40 — the server rolled the transaction back and said so.
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.TransactionRollback,
        // Class 53 — resource pressure that may pass.
        PostgresErrorCodes.InsufficientResources,
        PostgresErrorCodes.TooManyConnections,
        PostgresErrorCodes.OutOfMemory,
        PostgresErrorCodes.DiskFull,
        PostgresErrorCodes.ConfigurationLimitExceeded,
        // Class 55 — lock contention.
        PostgresErrorCodes.LockNotAvailable,
        PostgresErrorCodes.ObjectInUse,
        PostgresErrorCodes.ObjectNotInPrerequisiteState,
        // The server is still starting, so nothing ran.
        PostgresErrorCodes.CannotConnectNow,
        // Class 58 — server-side system errors reported before commit.
        PostgresErrorCodes.SystemError,
        PostgresErrorCodes.IoError
    };

    /// <summary>
    /// Every one of these is <c>IsTransient == true</c> to Npgsql, and every one of them means the connection
    /// was going away — which is exactly when a COMMIT can be in flight. Correct to retry for a SELECT, unsafe
    /// for an append. This list is the whole reason the write path does not just use
    /// <see cref="NpgsqlException.IsTransient" />.
    /// </summary>
    public static TheoryData<string> TransientToNpgsqlButUnknownOutcome => new()
    {
        PostgresErrorCodes.ConnectionException,
        PostgresErrorCodes.ConnectionDoesNotExist,
        PostgresErrorCodes.ConnectionFailure,
        PostgresErrorCodes.SqlClientUnableToEstablishSqlConnection,
        PostgresErrorCodes.SqlServerRejectedEstablishmentOfSqlConnection,
        PostgresErrorCodes.TransactionResolutionUnknown,
        PostgresErrorCodes.AdminShutdown,
        PostgresErrorCodes.CrashShutdown,
        PostgresErrorCodes.IdleSessionTimeout
    };

    /// <summary>Deterministic failures — a replay produces the identical error, so retrying is pure waste.</summary>
    public static TheoryData<string> DeterministicStates => new()
    {
        PostgresErrorCodes.UniqueViolation,
        PostgresErrorCodes.ForeignKeyViolation,
        PostgresErrorCodes.NotNullViolation,
        PostgresErrorCodes.SyntaxError,
        PostgresErrorCodes.UndefinedTable,
        PostgresErrorCodes.UndefinedColumn,
        PostgresErrorCodes.InvalidTextRepresentation,
        PostgresErrorCodes.InFailedSqlTransaction,
        PostgresErrorCodes.StatementCompletionUnknown,
        PostgresErrorCodes.QueryCanceled,
        PostgresErrorCodes.DatabaseDropped
    };

    [Theory]
    [MemberData(nameof(RetryableStates))]
    public void server_reported_and_transient_is_safe_to_retry(string sqlState)
        => WriteRetryClassifier.IsSafeToRetry(Postgres(sqlState)).ShouldBeTrue();

    [Theory]
    [MemberData(nameof(TransientToNpgsqlButUnknownOutcome))]
    public void npgsql_transient_but_outcome_unknown_is_not_retried(string sqlState)
        => WriteRetryClassifier.IsSafeToRetry(Postgres(sqlState)).ShouldBeFalse();

    [Theory]
    [MemberData(nameof(DeterministicStates))]
    public void deterministic_failures_are_not_retried(string sqlState)
        => WriteRetryClassifier.IsSafeToRetry(Postgres(sqlState)).ShouldBeFalse();

    [Fact]
    public void a_read_timeout_is_not_retried()
    {
        // The production shape from #5262: the connector timed out mid-batch, so the ROLLBACK never
        // reached the server and the transaction may well have committed.
        var e = new NpgsqlException("Exception while reading from stream",
            new TimeoutException("Timeout during reading attempt"));

        WriteRetryClassifier.IsSafeToRetry(e).ShouldBeFalse();
    }

    [Fact]
    public void pool_exhaustion_is_not_retried_because_it_cannot_be_told_apart_from_a_read_timeout()
    {
        // Npgsql reports pool exhaustion as NpgsqlException wrapping TimeoutException — the same object
        // graph as the read timeout above. One is safe to replay and one is not, so both surface.
        var e = new NpgsqlException(
            "The connection pool has been exhausted, either raise 'Max Pool Size' or 'Timeout'",
            new TimeoutException());

        WriteRetryClassifier.IsSafeToRetry(e).ShouldBeFalse();
    }

    [Fact]
    public void raw_io_failures_are_not_retried()
    {
        WriteRetryClassifier.IsSafeToRetry(new IOException("broken pipe")).ShouldBeFalse();
        WriteRetryClassifier.IsSafeToRetry(new SocketException(10054)).ShouldBeFalse();
        WriteRetryClassifier.IsSafeToRetry(new NpgsqlException("no inner")).ShouldBeFalse();
    }

    [Fact]
    public void the_real_cause_is_found_through_martens_own_wrapper()
    {
        // The lifetimes call TransformAndThrow before Polly sees anything, so the classifier has to
        // reach through MartenCommandException.
        var wrapped = new MartenCommandException(new NpgsqlCommand("select 1"),
            Postgres(PostgresErrorCodes.DeadlockDetected));
        WriteRetryClassifier.IsSafeToRetry(wrapped).ShouldBeTrue();

        var wrappedUnknown = new MartenCommandException(new NpgsqlCommand("select 1"),
            new NpgsqlException("stream", new TimeoutException()));
        WriteRetryClassifier.IsSafeToRetry(wrappedUnknown).ShouldBeFalse();
    }

    [Fact]
    public void an_aggregate_retries_only_when_every_inner_failure_is_safe()
    {
        var allSafe = new AggregateException(
            Postgres(PostgresErrorCodes.SerializationFailure),
            Postgres(PostgresErrorCodes.DeadlockDetected));
        WriteRetryClassifier.IsSafeToRetry(allSafe).ShouldBeTrue();

        // One unknown outcome poisons the whole batch — the safe inners tell us nothing about it.
        var mixed = new AggregateException(
            Postgres(PostgresErrorCodes.SerializationFailure),
            new NpgsqlException("stream", new TimeoutException()));
        WriteRetryClassifier.IsSafeToRetry(mixed).ShouldBeFalse();

        // An empty aggregate tells us nothing at all, so it is not a licence to replay.
        WriteRetryClassifier.IsSafeToRetry(new AggregateException()).ShouldBeFalse();
    }
}
