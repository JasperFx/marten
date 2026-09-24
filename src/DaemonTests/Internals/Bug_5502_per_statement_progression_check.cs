#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Progress;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

/// <summary>
/// #5502 — <c>OperationPage.ApplyCallbacksAsync</c> only ever ran the <c>AssertsOnCallback</c> check
/// for the operation at index 0. <c>UpdateProjectionProgress</c> is both <c>AssertsOnCallback</c> and
/// <c>NoDataReturnedCall</c>, so from index 1 on the loop hit <c>continue</c> before anything looked
/// at the marker and a stale-floor progression update was a silent no-op.
///
/// <para>
/// That is not a corner case: <c>ExecutionStage.ExecuteDownstreamAsync</c> records every composite
/// MEMBER's progress into the parent's shared batch, so a composite with N members puts N+1
/// progression operations on one page. The parent's is always index 0 — it is queued by
/// <c>StartProjectionBatchAsync</c> before any stage runs — so the parent was checked and every
/// member was not.
/// </para>
///
/// <para>
/// The naive fix does not work. <c>DbDataReader.RecordsAffected</c> is CUMULATIVE across the batch,
/// so a second progression update inherits the first one's row count and never sees its own zero.
/// The per-statement count is on <c>NpgsqlBatchCommand.RecordsAffected</c>, and Npgsql populates that
/// lazily as the reader advances — an operation's own count still reads -1 while the reader is parked
/// on an earlier statement. Hence a separate pass, after the reader is drained.
/// </para>
/// </summary>
public class Bug_5502_per_statement_progression_check: OneOffConfigurationsContext, IAsyncLifetime
{
    private readonly RecordingLogger theLogger = new();

    public override async ValueTask InitializeAsync()
    {
        StoreOptions(opts => opts.DotNetLogger = theLogger);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return base.DisposeAsync();
    }

    private async Task seedProgressionAt(long sequence, params string[] shardNames)
    {
        foreach (var name in shardNames)
        {
            theSession.QueueOperation(
                new InsertProjectionProgress(theStore.Events, new EventRange(new ShardName(name), sequence)));
        }

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private void queueUpdate(string shardName, long floor, long ceiling) =>
        theSession.QueueOperation(new UpdateProjectionProgress(theStore.Events,
            new EventRange(new ShardName(shardName), floor, ceiling, Substitute.For<ISubscriptionAgent>())));

    private async Task<long> progressionFor(string shardName) =>
        await theStore.Advanced.ProjectionProgressFor(new ShardName(shardName),
            token: TestContext.Current.CancellationToken);

    [Fact]
    public async Task a_stale_update_after_the_first_operation_is_detected()
    {
        await seedProgressionAt(12, "five", "six");

        queueUpdate("five", 12, 50);  // matches, affects 1 row
        queueUpdate("six", 5, 50);    // stale floor, affects 0 rows -- and is NOT at index 0

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Pre-fix nothing at all was reported for "six".
        var problem = theLogger.Warnings.ShouldHaveSingleItem()
            .Exception.ShouldBeOfType<ProgressionProgressOutOfOrderException>();
        problem.ProjectionName.ShouldBe(new ShardName("six").Identity);
        problem.ExpectedFloor.ShouldBe(5);
        problem.AttemptedCeiling.ShouldBe(50);

        // The write itself is unchanged -- this release reports the problem, it does not fail the batch.
        (await progressionFor("five")).ShouldBe(50);
        (await progressionFor("six")).ShouldBe(12);
    }

    [Fact]
    public async Task the_first_operations_cumulative_check_is_unreliable_and_the_pass_covers_it()
    {
        await seedProgressionAt(12, "seven", "eight");

        // "seven" is stale and FIRST. Its own statement affects no rows, but by the time its callback
        // reads the cumulative RecordsAffected, "eight" has already contributed a row -- so the
        // index-0 check that has always existed passes right over it.
        queueUpdate("seven", 5, 50);
        queueUpdate("eight", 12, 50);

        await Should.NotThrowAsync(() => theSession.SaveChangesAsync(TestContext.Current.CancellationToken));

        theLogger.Warnings.ShouldHaveSingleItem()
            .Exception.ShouldBeOfType<ProgressionProgressOutOfOrderException>()
            .ProjectionName.ShouldBe(new ShardName("seven").Identity);

        (await progressionFor("seven")).ShouldBe(12);
        (await progressionFor("eight")).ShouldBe(50);
    }

    [Fact]
    public async Task a_lone_stale_update_still_throws_exactly_as_it_always_has()
    {
        await seedProgressionAt(12, "nine");

        // Nothing else in the batch, so the cumulative count really is this statement's own and the
        // long-standing index-0 behaviour applies. Promoting the new pass to a throw is a breaking
        // change for a major; this one must not change now.
        queueUpdate("nine", 5, 50);

        await Should.ThrowAsync<ProgressionProgressOutOfOrderException>(() =>
            theSession.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task a_healthy_batch_of_several_progression_updates_reports_nothing()
    {
        await seedProgressionAt(12, "ten", "eleven", "twelve");

        queueUpdate("ten", 12, 50);
        queueUpdate("eleven", 12, 50);
        queueUpdate("twelve", 12, 50);

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        theLogger.Warnings.ShouldBeEmpty();

        (await progressionFor("ten")).ShouldBe(50);
        (await progressionFor("eleven")).ShouldBe(50);
        (await progressionFor("twelve")).ShouldBe(50);
    }

    [Fact]
    public async Task an_ordinary_document_batch_is_untouched()
    {
        // The new pass runs on every page in the store, so prove it stays out of the way of a batch
        // that carries no progression operations at all -- no warnings, and the writes still land.
        theSession.Store(new Target5502 { Id = Guid.NewGuid(), Name = "a" });
        theSession.Store(new Target5502 { Id = Guid.NewGuid(), Name = "b" });
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        theLogger.Warnings.ShouldBeEmpty();
        (await theSession.Query<Target5502>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task a_document_write_mixed_in_with_a_stale_progression_update_still_commits()
    {
        await seedProgressionAt(12, "thirteen");

        theSession.Store(new Target5502 { Id = Guid.NewGuid(), Name = "mixed" });
        queueUpdate("thirteen", 12, 50);
        queueUpdate("fourteen", 5, 50);   // no such row at all -- affects 0

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        theLogger.Warnings.ShouldHaveSingleItem()
            .Exception.ShouldBeOfType<ProgressionProgressOutOfOrderException>()
            .ProjectionName.ShouldBe(new ShardName("fourteen").Identity);

        // The document operation's own result-set handling is unaffected by the extra pass.
        (await theSession.Query<Target5502>().CountAsync(x => x.Name == "mixed",
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await progressionFor("thirteen")).ShouldBe(50);
    }
}

public class Target5502
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Captures what <see cref="Marten.Internal.Sessions.OperationPage" /> reports. Assigned through
/// <c>StoreOptions.DotNetLogger</c>, which is the fallback the page uses when no ILoggerFactory is
/// registered.
/// </summary>
public class RecordingLogger: ILogger
{
    public record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries) return _entries.ToList();
        }
    }

    public IReadOnlyList<Entry> Warnings => Entries.Where(x => x.Level == LogLevel.Warning).ToList();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
    }
}
