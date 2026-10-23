using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using DaemonTests.TestingSupport;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

/// <summary>
/// #5741 (jasperfx#1029 part 2). Marten used to register all of <see cref="NpgsqlException" /> and
/// <c>MartenCommandException</c> as transient for projection code. The classification now comes from the
/// exception itself (<see cref="TransientFailures" />, Npgsql's SQLSTATE reading), and jasperfx#1064 keeps a
/// permanent database error from being dead-lettered under <c>SkipApplyErrors</c>.
/// </summary>
/// <remarks>
/// <para>
/// What the registration actually decided, measured rather than assumed: whether an exception from projection
/// code is wrapped as an <see cref="ApplyEventException" />. A failure while applying events is never retried
/// either way -- the daemon's resilience pipeline is around the batch commit, not the apply -- so the shard
/// pauses on the first attempt in both versions. What changes is the report: a permanent database error used to
/// pause the shard as <see cref="ShardFailureCategory.Other" /> with no event attached, and now pauses it as
/// <see cref="ShardFailureCategory.ApplyEvent" /> naming the event that hit it.
/// </para>
/// <para>
/// Every database fact runs with and without <c>SkipApplyErrors</c>: the narrowing was unsafe before
/// jasperfx#1064 precisely because, with it on, a permanent error would have been dead-lettered event after
/// event.
/// </para>
/// </remarks>
public class database_errors_in_projection_code: OneOffConfigurationsContext
{
    private DatabaseFailingProjection theProjection = null!;

    private async Task<Guid> ArrangeAsync(bool skipApplyErrors, Func<int, Exception?> failure, string name = "probe")
    {
        theProjection = new DatabaseFailingProjection(failure);

        StoreOptions(opts =>
        {
            opts.Projections.Errors.SkipApplyErrors = skipApplyErrors;
            opts.Projections.Add(theProjection, ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(DatabaseProbe));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(DeadLetterEvent));

        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(streamId, new DatabaseProbeEvent(name));
        await session.SaveChangesAsync();

        return streamId;
    }

    private async Task<int> DeadLetterCountAsync()
    {
        await using var query = theStore.QuerySession();
        return await query.Query<DeadLetterEvent>().CountAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting until {description}");
    }

    // A running daemon rather than CatchUpAsync, which can return before a faulted shard's failure is observable

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task a_pool_timeout_or_connection_failure_stays_transient_and_is_not_dead_lettered(
        bool skipApplyErrors)
    {
        // What Npgsql throws for an exhausted pool or a connection it cannot open: an NpgsqlException carrying
        // the TimeoutException or SocketException that caused it. Narrowing the registration must not turn this
        // into an event's fault.
        await ArrangeAsync(skipApplyErrors, _ =>
            new NpgsqlException("The connection pool has been exhausted", new TimeoutException()));

        var state = await StartAndWaitForPauseAsync();

        state.Failure.ShouldNotBeNull().IsTransient.ShouldBeTrue();
        state.Exception.ShouldNotBeOfType<ApplyEventException>();
        (await DeadLetterCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task an_undefined_column_in_projection_code_pauses_the_shard_naming_the_event_without_dead_lettering(
        bool skipApplyErrors)
    {
        await ArrangeAsync(skipApplyErrors, _ => null, DatabaseFailingProjection.MissingColumn);

        var state = await StartAndWaitForPauseAsync();

        // The real cause, reachable from what the daemon reports
        state.Exception.ShouldNotBeNull().Flatten().OfType<PostgresException>()
            .ShouldContain(x => x.SqlState == PostgresErrorCodes.UndefinedColumn);

        var failure = state.Failure.ShouldNotBeNull();
        failure.IsTransient.ShouldBeFalse();
        failure.RootExceptionType.ShouldBe(typeof(PostgresException).FullName);

        // The difference #5741 makes: attributed to the event that hit it, where the blanket registration
        // reported an unattributed failure (Other, no event)
        failure.Category.ShouldBe(ShardFailureCategory.ApplyEvent);
        failure.Event.ShouldNotBeNull().Sequence.ShouldBe(1);

        theProjection.Attempts.ShouldBe(1);

        // Not this event's fault, so not dead-lettered -- even under SkipApplyErrors (jasperfx#1064)
        (await DeadLetterCountAsync()).ShouldBe(0);
    }

    private async Task<ShardState> StartAndWaitForPauseAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var paused = new ShardActionWatcher(daemon.Tracker, "DatabaseFailing:All", ShardAction.Paused,
            TimeSpan.FromSeconds(60));

        await daemon.StartAllAsync();
        return await paused.Task;
    }

    [Fact]
    public async Task the_projections_own_failure_is_still_dead_lettered_under_skip_apply_errors()
    {
        await ArrangeAsync(true, _ => new InvalidOperationException("the projection's own bug"));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await WaitUntilAsync(async () => await DeadLetterCountAsync() == 1, "the event is dead-lettered");

        theProjection.Attempts.ShouldBe(1);
    }
}

public record DatabaseProbeEvent(string Name);

public class DatabaseProbe
{
    public Guid Id { get; set; }
}

public class DatabaseFailingProjection: EventProjection
{
    public const string MissingColumn = "missing-column";

    private readonly Func<int, Exception?> _failure;
    private int _attempts;

    public DatabaseFailingProjection(Func<int, Exception?> failure)
    {
        _failure = failure;
        Name = "DatabaseFailing";
        IncludeType<DatabaseProbeEvent>();
    }

    public int Attempts => _attempts;

    public override async ValueTask ApplyAsync(IDocumentOperations operations, IEvent e, CancellationToken cancellation)
    {
        if (e.Data is not DatabaseProbeEvent probe) return;

        var attempt = Interlocked.Increment(ref _attempts);

        if (probe.Name == MissingColumn)
        {
            // A real 42703 from PostgreSQL, through Marten's own command path
            await operations.QueryAsync<int>("select no_such_column from mt_events", cancellation);
        }

        if (_failure(attempt) is { } exception) throw exception;

        operations.Store(new DatabaseProbe { Id = e.Id });
    }
}

internal static class ExceptionGraph
{
    public static System.Collections.Generic.IEnumerable<Exception> Flatten(this Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten)) yield return inner;
        }
        else if (exception.InnerException != null)
        {
            foreach (var inner in exception.InnerException.Flatten()) yield return inner;
        }
    }
}
