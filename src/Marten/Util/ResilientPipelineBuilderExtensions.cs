#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Npgsql;
using Polly;
using Polly.Retry;

namespace Marten.Util;

internal static class ResilientPipelineBuilderExtensions
{
    public static ResiliencePipelineBuilder AddMartenDefaults(this ResiliencePipelineBuilder builder)
    {
        #region sample_default_polly_setup

        // default Marten policies. A command that timed out is not retried: it has already spent
        // the whole CommandTimeout, and every retry would hold the caller for that long again.
        return builder
           .AddRetry(new()
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<NpgsqlException>(e => !IsTimeout(e))
                    .Handle<MartenCommandException>(e => !IsTimeout(e))
                    .Handle<EventLoaderException>(e => !IsTimeout(e)),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential
            });

        #endregion
    }

    /// <summary>
    ///     Npgsql reports a command timeout as an <see cref="NpgsqlException" /> wrapping a
    ///     <see cref="TimeoutException" />, which Marten may wrap again in <see cref="MartenCommandException" />.
    /// </summary>
    internal static bool IsTimeout(Exception exception)
    {
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is TimeoutException) return true;
        }

        return false;
    }

    /// <summary>
    ///     The retry policy for committing a unit of work. See <see cref="WriteRetryClassifier" /> for why this
    ///     is far narrower than <see cref="AddMartenDefaults" />.
    /// </summary>
    public static ResiliencePipelineBuilder AddMartenWriteDefaults(this ResiliencePipelineBuilder builder)
    {
        #region sample_default_write_polly_setup

        // Marten's policy for committing a unit of work. A commit is NOT idempotent -- replaying it
        // appends the same events a second time -- so unlike the read policy, this one retries only
        // when the previous attempt is KNOWN to have left nothing behind.
        return builder
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is { } e && WriteRetryClassifier.IsSafeToRetry(e)),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(50),
                BackoffType = DelayBackoffType.Exponential
            });

        #endregion
    }
}

/// <summary>
///     Decides whether a failed unit-of-work commit may be replayed.
/// </summary>
/// <remarks>
///     <para>
///         A Marten commit is one transaction carrying document writes <b>and</b> event appends. Appends are not
///         idempotent: replaying a batch that already committed server-side appends the events a second time, and
///         nothing downstream can tell the difference. So the bar for retrying a write is not "was this error
///         transient" but "do we <i>know</i> the previous attempt left nothing behind".
///     </para>
///     <para>
///         Only PostgreSQL can tell us that. If the server answered with an error, the transaction is definitively
///         aborted and a retry starts from a clean slate. If the failure happened at the I/O layer -- a read
///         timeout, a dropped connection -- the ROLLBACK never reached the server, and a client cannot distinguish
///         "the transaction rolled back" from "the transaction committed and the response was lost". That case is
///         surfaced to the caller rather than guessed at.
///     </para>
///     <para>
///         <b>This is deliberately not <see cref="NpgsqlException.IsTransient" />.</b> Npgsql's notion of transient
///         is built for idempotent work, so it includes the whole 08xxx connection class plus
///         <c>admin_shutdown</c>, <c>crash_shutdown</c>, <c>idle_session_timeout</c> and
///         <c>transaction_resolution_unknown</c> -- every one of which means the connection died, possibly with a
///         COMMIT in flight. Correct for a SELECT, unsafe for an append. The read pipeline
///         (<see cref="ResilientPipelineBuilderExtensions.AddMartenDefaults" />) keeps the broad behaviour.
///     </para>
///     <para>
///         It is also not possible to recover the "nothing was ever sent" cases by inspecting the exception. Npgsql
///         reports connection-pool exhaustion as an <see cref="NpgsqlException" /> wrapping a
///         <see cref="TimeoutException" /> -- structurally identical to a read timeout that stalled halfway through
///         a batch. One is safe to replay and one is not, and they are the same object graph, so the allowlist
///         below is the only honest place to draw the line.
///     </para>
///     <para>
///         <b>"Nothing was left behind" is necessary but not sufficient (#5528).</b> <c>40001</c>
///         <c>serialization_failure</c> satisfies it perfectly -- the server rolled the transaction back and said
///         so -- and retrying it was still wrong, because Marten replays the <i>already-computed</i> operations
///         rather than re-running the application code that produced them. A serialization failure means precisely
///         that the snapshot those operations were computed from is no longer a valid basis for them. The replay
///         opens a NEW transaction with a FRESH snapshot, so the conflict PostgreSQL correctly detected is gone and
///         the write lands -- converting a caught conflict into a silent lost update. Observed as exactly that: two
///         <c>Serializable</c> sessions each loading a document at 100, one committing 100-500, the other
///         committing 100-350 on top of it and reporting success. Only the application can resolve a 40001, by
///         re-reading and recomputing, so it is surfaced as <c>ConcurrentUpdateException</c>.
///     </para>
///     <para>
///         Note this reasoning is about the <i>snapshot</i>, not about isolation levels as such, which is why
///         <c>40P01</c> <c>deadlock_detected</c> stays retryable: a deadlock aborts over lock ordering, not over a
///         snapshot the server has rejected, and replaying it is the textbook remedy at Marten's default
///         <c>ReadCommitted</c>. A deadlock retry under <c>Serializable</c> replays stale-snapshot work for the
///         same structural reason 40001 did, and the honest fix there is to recompute rather than replay -- but
///         that needs the isolation level plumbed into this classifier, and no one has observed it biting, so it is
///         left as a known gap rather than guessed at here.
///     </para>
/// </remarks>
internal static class WriteRetryClassifier
{
    /// <summary>
    ///     SQLSTATEs where PostgreSQL reported the failure itself (so the transaction is gone) <i>and</i> the
    ///     condition is one a later attempt can plausibly get past.
    /// </summary>
    private static readonly FrozenSet<string> s_retryable = new[]
    {
        // Class 40 -- transaction rollback. The server rolled it back and said so.
        // NOTE 40001 is deliberately NOT here; see s_staleSnapshot below.
        PostgresErrorCodes.DeadlockDetected,              // 40P01
        PostgresErrorCodes.TransactionRollback,           // 40000

        // Class 53 -- insufficient resources. Nothing committed; the pressure may pass.
        PostgresErrorCodes.InsufficientResources,         // 53000
        PostgresErrorCodes.DiskFull,                      // 53100
        PostgresErrorCodes.OutOfMemory,                   // 53200
        PostgresErrorCodes.TooManyConnections,            // 53300
        PostgresErrorCodes.ConfigurationLimitExceeded,    // 53400

        // Class 55 -- lock contention. The classic "try again in a moment".
        PostgresErrorCodes.ObjectNotInPrerequisiteState,  // 55000
        PostgresErrorCodes.ObjectInUse,                   // 55006
        PostgresErrorCodes.LockNotAvailable,              // 55P03

        // 57P03 -- the server is still starting up, so the statement never ran. Note the rest of class 57
        // (admin_shutdown, crash_shutdown, database_dropped, idle_session_timeout, query_canceled) is
        // deliberately absent: those arrive as the connection is being torn down, which is exactly when a
        // COMMIT can be in flight.
        PostgresErrorCodes.CannotConnectNow,              // 57P03

        // Class 58 -- server-side system errors, reported by the server before commit.
        PostgresErrorCodes.SystemError,                   // 58000
        PostgresErrorCodes.IoError                        // 58030
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    ///     SQLSTATEs where the server rolled the transaction back <i>because the snapshot the work was computed
    ///     from is no longer valid</i>. "Nothing was left behind" is true here and still not a licence to replay:
    ///     see <see cref="Outcome.StaleSnapshot" />.
    /// </summary>
    private static readonly FrozenSet<string> s_staleSnapshot = new[]
    {
        PostgresErrorCodes.SerializationFailure // 40001
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The four things a failed commit can tell us about the transaction it left behind.</summary>
    private enum Outcome
    {
        /// <summary>Known to be rolled back, and the cause may not recur. Replay is safe.</summary>
        RolledBack,

        /// <summary>Known to be rolled back, but a replay reproduces it exactly. Retrying is waste.</summary>
        Deterministic,

        /// <summary>
        ///     Known to be rolled back, and a replay would most likely <b>succeed</b> — which is exactly why it
        ///     must not be replayed. See the remarks on <see cref="WriteRetryClassifier" />.
        /// </summary>
        StaleSnapshot,

        /// <summary>The wire failed. The transaction may have committed and we cannot find out.</summary>
        Unknown
    }

    public static bool IsSafeToRetry(Exception exception) => Classify(exception) == Outcome.RolledBack;

    private static Outcome Classify(Exception exception)
    {
        var sawWireFailure = false;
        var sawPostgresError = false;
        var everyPostgresErrorIsRetryable = true;
        var sawRetryableMartenFailure = false;
        var sawStaleSnapshot = false;

        foreach (var node in Flatten(exception))
        {
            switch (node)
            {
                // Must precede NpgsqlException -- PostgresException derives from it.
                case PostgresException pg:
                    sawPostgresError = true;
                    if (pg.SqlState is { } staleState && s_staleSnapshot.Contains(staleState))
                    {
                        sawStaleSnapshot = true;
                    }

                    if (pg.SqlState is not { } state || !s_retryable.Contains(state))
                    {
                        everyPostgresErrorIsRetryable = false;
                    }

                    break;

                // The wire. Npgsql surfaces a mid-batch read timeout, a dropped connection AND
                // connection-pool exhaustion through these, and the first two leave the outcome unknowable.
                case NpgsqlException:
                case TimeoutException:
                case IOException:
                case SocketException:
                case OperationCanceledException:
                    sawWireFailure = true;
                    break;

                // The two Marten-side wrappers the pre-#5262 policy retried. These reach us when the batch
                // got to the server and came back, and Marten's own result handling then threw -- an
                // IStorageOperation.PostprocessAsync failing to read its results, say. ExecuteBatchPagesAsync
                // rolls the transaction back itself there, so the outcome is known and a replay is safe.
                // CoreTests.retry_mechanism pins this.
                case MartenCommandException:
                case EventLoaderException:
                    sawRetryableMartenFailure = true;
                    break;
            }
        }

        // Any single unknown outcome poisons the batch, no matter what else failed cleanly alongside it.
        if (sawWireFailure) return Outcome.Unknown;

        // Likewise one stale-snapshot failure: the whole unit of work was computed from that snapshot, so a
        // sibling page failing "retryably" says nothing useful about replaying this one.
        if (sawStaleSnapshot) return Outcome.StaleSnapshot;

        if (sawPostgresError)
        {
            return everyPostgresErrorIsRetryable ? Outcome.RolledBack : Outcome.Deterministic;
        }

        // Everything else is a domain failure Marten raised deliberately -- ConcurrencyException,
        // DocumentAlreadyExistsException, EventStreamUnexpectedMaxEventIdException. Replaying reproduces it
        // exactly, and the pre-#5262 policy never retried these either: it handled only the three types
        // named in AddMartenDefaults.
        return sawRetryableMartenFailure ? Outcome.RolledBack : Outcome.Deterministic;
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            // ExecuteBatchPagesAsync aggregates per-operation failures.
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var nested in Flatten(inner))
                {
                    yield return nested;
                }
            }
        }
        else if (exception.InnerException is { } wrapped)
        {
            foreach (var nested in Flatten(wrapped))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    ///     Exposed so tests can assert the classification table rather than re-deriving it.
    /// </summary>
    internal static IReadOnlyCollection<string> RetryableSqlStates => s_retryable;
}
