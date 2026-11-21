using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace DaemonTests.Resiliency;

/// <summary>
/// jasperfx#1256. A batch whose build fails transiently -- here, what Npgsql throws for an exhausted connection pool --
/// is built again through Marten's daemon batch pipeline under every error policy, and the shard pauses with
/// <see cref="ShardFailure.IsTransient" /> true only once that pipeline gives up. The pipeline is Marten's own
/// (<see cref="ResilientPipelineBuilderExtensions.AddMartenDaemonBatchDefaults" />), not the session pipeline, which
/// does not retry timeouts.
/// </summary>
public class transient_batch_build_failures: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    // The first build, and every retry the pipeline allows
    private const int AllAttempts = 1 + ResilientPipelineBuilderExtensions.DaemonBatchRetryAttempts;

    private static Exception poolTimeout() =>
        new NpgsqlException("The connection pool has been exhausted", new TimeoutException());

    private Guid theStream;

    public override async ValueTask DisposeAsync()
    {
        FlakyBatchFaults.Forget(theStream);
        await base.DisposeAsync();
    }

    private async Task arrangeAsync(ProjectionErrorAction policy, Action<StoreOptions> projection)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = policy;
            projection(opts);
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        theStream = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(theStream, new DatabaseProbeEvent("probe"));
        await session.SaveChangesAsync();
    }

    private async Task<int> deadLettersAsync()
    {
        await using var query = theStore.QuerySession();
        return await query.Query<DeadLetterEvent>().CountAsync();
    }

    private async Task<int> parkedAsync()
        => (await ((IEventDatabase)theStore.Storage.Database).FetchParkedIdentitiesAsync(new ParkedIdentityQuery(),
            TestContext.Current.CancellationToken)).Count;

    [Theory]
    [InlineData(ProjectionErrorAction.Park)]
    [InlineData(ProjectionErrorAction.Pause)]
    [InlineData(ProjectionErrorAction.Skip)]
    public async Task a_transient_failure_while_building_an_aggregation_batch_is_built_again(
        ProjectionErrorAction policy)
    {
        await arrangeAsync(policy, opts => opts.Projections.Snapshot<FlakyBatchAggregate>(SnapshotLifecycle.Async));
        FlakyBatchFaults.Fail(theStream, 2);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var paused = new ShardActionWatcher(daemon.Tracker, "FlakyBatchAggregate:All", ShardAction.Paused, Generous);
        await daemon.StartAllAsync();

        await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);

        // Failed twice, and the third build went through
        FlakyBatchFaults.Attempts(theStream).ShouldBe(3);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<FlakyBatchAggregate>(theStream)).ShouldNotBeNull().Applied.ShouldBe(1);

        paused.Task.IsCompleted.ShouldBeFalse();
        (await parkedAsync()).ShouldBe(0);
        (await deadLettersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task a_transient_failure_while_building_an_event_projection_batch_is_built_again()
    {
        var projection = new DatabaseFailingProjection(attempt => attempt <= 2 ? poolTimeout() : null);
        await arrangeAsync(ProjectionErrorAction.Park, opts => opts.Projections.Add(projection, ProjectionLifecycle.Async));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var paused = new ShardActionWatcher(daemon.Tracker, "DatabaseFailing:All", ShardAction.Paused, Generous);
        await daemon.StartAllAsync();

        await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);

        projection.Attempts.ShouldBe(3);

        await using var query = theStore.QuerySession();
        (await query.Query<DatabaseProbe>().CountAsync()).ShouldBe(1);
        paused.Task.IsCompleted.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ProjectionErrorAction.Park)]
    [InlineData(ProjectionErrorAction.Pause)]
    [InlineData(ProjectionErrorAction.Skip)]
    public async Task a_transient_failure_that_outlasts_the_batch_pipeline_pauses_as_transient(
        ProjectionErrorAction policy)
    {
        await arrangeAsync(policy, opts => opts.Projections.Snapshot<FlakyBatchAggregate>(SnapshotLifecycle.Async));
        FlakyBatchFaults.Fail(theStream, int.MaxValue);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        var paused = new ShardActionWatcher(daemon.Tracker, "FlakyBatchAggregate:All", ShardAction.Paused, Generous);
        await daemon.StartAllAsync();

        var state = await paused.Task;

        state.Failure.ShouldNotBeNull().IsTransient.ShouldBeTrue();
        FlakyBatchFaults.Attempts(theStream).ShouldBe(AllAttempts);

        // Not the event's fault, so neither parked nor dead-lettered
        (await parkedAsync()).ShouldBe(0);
        (await deadLettersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task a_failure_that_is_not_transient_is_not_built_again()
    {
        await arrangeAsync(ProjectionErrorAction.Park,
            opts => opts.Projections.Snapshot<FlakyBatchAggregate>(SnapshotLifecycle.Async));
        FlakyBatchFaults.Fail(theStream, int.MaxValue, () => new InvalidOperationException("a bug in the projection"));

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var deadline = DateTimeOffset.UtcNow.Add(Generous);
        while (DateTimeOffset.UtcNow < deadline && await parkedAsync() == 0)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Parked on the first build, never handed to the pipeline
        (await parkedAsync()).ShouldBe(1);
        FlakyBatchFaults.Attempts(theStream).ShouldBe(1);
    }
}

/// <summary>Fails its stream's event the way <see cref="FlakyBatchFaults" /> says, then applies it.</summary>
public partial class FlakyBatchAggregate
{
    public Guid Id { get; set; }
    public int Applied { get; set; }

    public void Apply(IEvent<DatabaseProbeEvent> e)
    {
        FlakyBatchFaults.Visit(e.StreamId);
        Applied++;
    }
}

public static class FlakyBatchFaults
{
    private static readonly ConcurrentDictionary<Guid, Fault> _faults = new();

    public static void Fail(Guid stream, int times, Func<Exception>? exception = null)
        => _faults[stream] = new Fault(times,
            exception ?? (() => new NpgsqlException("The connection pool has been exhausted", new TimeoutException())));

    public static int Attempts(Guid stream) => _faults.TryGetValue(stream, out var fault) ? fault.Attempts : 0;

    public static void Forget(Guid stream) => _faults.TryRemove(stream, out _);

    public static void Visit(Guid stream)
    {
        if (!_faults.TryGetValue(stream, out var fault)) return;

        var attempt = Interlocked.Increment(ref fault.Attempts);
        if (attempt <= fault.Times) throw fault.Exception();
    }

    private sealed class Fault(int times, Func<Exception> exception)
    {
        public readonly int Times = times;
        public readonly Func<Exception> Exception = exception;
        public int Attempts;
    }
}
