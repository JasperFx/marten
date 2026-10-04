#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Polly;
using Shouldly;
using Xunit;

namespace CoreTests;

/// <summary>
/// #5585. The write pipeline carries one <see cref="DocumentSessionBase.PagesExecution"/> across every retry
/// attempt, and its exception list used to be built once in an initializer — so attempt 2 appended to the list
/// attempt 1 had already filled.
///
/// That matters because ExecuteBatchPagesAsync branches on the <i>count</i>: exactly one exception is rethrown
/// as itself, more than one becomes an AggregateException. A single operation failing the same way twice
/// therefore changed the exception the caller saw, purely as a function of how many times the batch was
/// retried.
/// </summary>
public class Bug_5585_write_retry_exception_list
{
    /// <summary>
    /// Stands in for a connection lifetime whose batch always fails the same retryable way, recording one
    /// per-operation exception into the list it is handed — exactly what the real lifetimes do before they
    /// branch on the count.
    /// </summary>
    private sealed class AlwaysFailsOnce: IConnectionLifetime
    {
        public int Attempts { get; private set; }
        public List<List<Exception>> ListsSeen { get; } = new();

        public Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions,
            CancellationToken token, IReadOnlyList<ITransactionParticipant>? participants = null)
        {
            Attempts++;
            ListsSeen.Add(exceptions);
            exceptions.Add(new InvalidOperationException($"operation failed on attempt {Attempts}"));

            // 40P01 deadlock_detected — WriteRetryClassifier's canonical "rolled back, try again".
            throw new PostgresException("deadlock", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);
        }

        public IMartenSessionLogger Logger { get; set; } = new NulloMartenLogger();
        public int CommandTimeout => 30;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public int Execute(NpgsqlCommand cmd) => throw new NotSupportedException();
        public Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = default) =>
            throw new NotSupportedException();
        public DbDataReader ExecuteReader(NpgsqlCommand command) => throw new NotSupportedException();
        public Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default) =>
            throw new NotSupportedException();
        public DbDataReader ExecuteReader(NpgsqlBatch batch) => throw new NotSupportedException();
        public Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default) =>
            throw new NotSupportedException();
    }

    private static async Task<AlwaysFailsOnce> RunThroughTheWritePipeline()
    {
        var lifetime = new AlwaysFailsOnce();
        var execution = new DocumentSessionBase.PagesExecution([], lifetime, null);
        var pipeline = new ResiliencePipelineBuilder().AddMartenWriteDefaults().Build();

        await Should.ThrowAsync<PostgresException>(async () => await pipeline.ExecuteAsync(
            static (e, t) => new ValueTask(
                e.Connection.ExecuteBatchPagesAsync(e.Pages, e.BeginAttempt(), t, e.Participants)),
            execution, CancellationToken.None));

        // the retry budget is the point of the exercise -- 1 attempt + 3 retries
        lifetime.Attempts.ShouldBe(4);
        return lifetime;
    }

    [Fact]
    public async Task each_attempt_gets_a_list_of_its_own()
    {
        var lifetime = await RunThroughTheWritePipeline();

        foreach (var list in lifetime.ListsSeen)
        {
            list.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task no_two_attempts_share_the_same_list_instance()
    {
        var lifetime = await RunThroughTheWritePipeline();

        lifetime.ListsSeen.Distinct(ReferenceEqualityComparer.Instance).Count().ShouldBe(4);
    }

    /// <summary>
    /// The pre-fix shape, kept as an executable explanation of what went wrong: when every attempt is handed
    /// the one list hanging off the record, four attempts leave four exceptions in it. ExecuteBatchPagesAsync
    /// reads that count to decide between rethrowing the single failure and wrapping in an AggregateException,
    /// so the caller's exception type moved with the retry count. This drives the old delegate deliberately —
    /// production no longer builds it this way.
    /// </summary>
    [Fact]
    public async Task the_old_shared_list_accumulated_across_attempts()
    {
        var lifetime = new AlwaysFailsOnce();
        var execution = new DocumentSessionBase.PagesExecution([], lifetime, null);
        var pipeline = new ResiliencePipelineBuilder().AddMartenWriteDefaults().Build();

        await Should.ThrowAsync<PostgresException>(async () => await pipeline.ExecuteAsync(
            static (e, t) => new ValueTask(
                e.Connection.ExecuteBatchPagesAsync(e.Pages, e.Exceptions, t, e.Participants)),
            execution, CancellationToken.None));

        lifetime.Attempts.ShouldBe(4);
        execution.Exceptions.Count.ShouldBe(4,
            "this is the behaviour BeginAttempt() exists to prevent");
    }
}
