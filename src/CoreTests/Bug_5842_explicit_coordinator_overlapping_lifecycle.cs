using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace CoreTests;

// #5842, the Marten side of jasperfx#1148.
//
// JasperFx's RunCommand stops the host again when ApplicationStopping fires, so a WebApplicationFactory/Alba
// teardown has two IHost.StopAsync calls in flight and both reach the projection coordinator. ProjectionCoordinator
// gets serialized lifecycle calls from ProjectionCoordinatorBase (jasperfx#1148), but ExplicitProjectionCoordinator
// does not derive from it. Unserialized, overlapping stops both took the same daemon snapshot, ran StopAllAsync on
// the same daemons at once and disposed each one twice.
//
// No database is touched: the store is only constructed, and the coordinator's cache is seeded with fake daemons.
public class Bug_5842_explicit_coordinator_overlapping_lifecycle
{
    private static readonly TimeSpan OverlapTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task overlapping_stops_and_pauses_stop_and_dispose_each_daemon_once()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        const int callers = 16;

        for (var round = 0; round < 100; round++)
        {
            var logger = new CapturingLogger();
            var coordinator = new ExplicitProjectionCoordinator(store, logger);
            var daemons = Enumerable.Range(0, 3).Select(_ => new FakeDaemon()).ToArray();
            for (var i = 0; i < daemons.Length; i++)
            {
                coordinator.UseDaemon($"db{i}", daemons[i]);
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = Enumerable.Range(0, callers)
                .Select(i => Task.Run(async () =>
                {
                    await gate.Task;
                    if (i % 2 == 0)
                    {
                        await coordinator.StopAsync(CancellationToken.None);
                    }
                    else
                    {
                        await coordinator.PauseAsync();
                    }
                }))
                .ToArray();

            gate.SetResult();
            // Bounded so a regression fails instead of hanging the run.
            await Should.NotThrowAsync(() => Task.WhenAll(calls).WaitAsync(OverlapTimeout));

            foreach (var daemon in daemons)
            {
                daemon.StopAllCount.ShouldBe(1);
                daemon.DisposeCount.ShouldBe(1);
            }

            logger.Errors.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task a_daemon_resolved_while_a_stop_is_in_flight_is_not_dropped_undisposed()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        var coordinator = new ExplicitProjectionCoordinator(store, new CapturingLogger());

        var late = new FakeDaemon();
        // An accessor (DaemonForDatabase and friends) resolving a daemon while the stop is still fanning out.
        var first = new FakeDaemon { OnStopAll = () => coordinator.UseDaemon("late", late) };
        coordinator.UseDaemon("first", first);

        await coordinator.StopAsync(CancellationToken.None);

        // The stop used to clear the cache after its fan-out, which dropped the late daemon without stopping or
        // disposing it. It must still be in the cache, so the next stop tears it down.
        late.DisposeCount.ShouldBe(0);
        await coordinator.StopAsync(CancellationToken.None);
        late.StopAllCount.ShouldBe(1);
        late.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task a_start_overlapping_a_stop_never_starts_a_disposed_daemon()
    {
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        const int callers = 16;

        for (var round = 0; round < 100; round++)
        {
            var coordinator = new ExplicitProjectionCoordinator(store, new CapturingLogger());
            var daemons = Enumerable.Range(0, 3).Select(_ => new FakeDaemon()).ToArray();
            for (var i = 0; i < daemons.Length; i++)
            {
                coordinator.UseDaemon($"db{i}", daemons[i]);
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = Enumerable.Range(0, callers)
                .Select(i => Task.Run(async () =>
                {
                    await gate.Task;
                    if (i % 2 == 0)
                    {
                        await coordinator.StopAsync(CancellationToken.None);
                    }
                    else
                    {
                        await coordinator.ResumeAsync();
                    }
                }))
                .ToArray();

            gate.SetResult();
            await Should.NotThrowAsync(() => Task.WhenAll(calls).WaitAsync(OverlapTimeout));

            foreach (var daemon in daemons)
            {
                daemon.StartedAfterDispose.ShouldBeFalse();
                daemon.DisposeCount.ShouldBe(1);
            }
        }
    }

    private sealed class CapturingLogger: ILogger<ExplicitProjectionCoordinator>
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors)
                {
                    return _errors.ToArray();
                }
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;

            lock (_errors)
            {
                _errors.Add(formatter(state, exception));
            }
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    // IProjectionDaemon is large; the coordinator only touches StartAllAsync, StopAllAsync and Dispose. The rest
    // throw so an accidental new dependency surfaces loudly.
    private sealed class FakeDaemon: IProjectionDaemon
    {
        private int _stopAllCount;
        private int _disposeCount;
        private int _startedAfterDispose;

        public Action? OnStopAll { get; init; }

        public int StopAllCount => Volatile.Read(ref _stopAllCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool StartedAfterDispose => Volatile.Read(ref _startedAfterDispose) == 1;

        public async Task StopAllAsync()
        {
            Interlocked.Increment(ref _stopAllCount);
            OnStopAll?.Invoke();

            // Like the real daemon, stopping awaits; this is the window an overlapping caller used to slip into.
            await Task.Yield();

            if (DisposeCount > 0)
            {
                // The real daemon throws here once disposed (marten#5055); a caller that stopped a daemon another
                // caller had already disposed would log it as "Error while trying to stop daemon agents".
                throw new ObjectDisposedException(nameof(FakeDaemon));
            }
        }

        public async Task StartAllAsync()
        {
            await Task.Yield();
            if (DisposeCount > 0)
            {
                Interlocked.Exchange(ref _startedAfterDispose, 1);
            }
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);

        // ---- Unused by the explicit coordinator ----
        public Task StartAgentAsync(string shardName, CancellationToken token) => throw new NotSupportedException();
        public Task StopAgentAsync(string shardName, Exception? ex = null) => throw new NotSupportedException();
        public AgentStatus StatusFor(string shardName) => throw new NotSupportedException();
        public IReadOnlyList<ISubscriptionAgent> CurrentAgents() => throw new NotSupportedException();
        public bool HasAnyPaused() => throw new NotSupportedException();
        public void EjectPausedShard(string shardName) => throw new NotSupportedException();
        public Task PrepareForRebuildsAsync() => throw new NotSupportedException();
        public ShardStateTracker Tracker => throw new NotSupportedException();
        public bool IsRunning => throw new NotSupportedException();
        public Task RebuildProjectionAsync(string projectionName, CancellationToken token) => throw new NotSupportedException();
        public Task RebuildProjectionAsync<TView>(CancellationToken token) => throw new NotSupportedException();
        public Task RebuildProjectionAsync(Type projectionType, CancellationToken token) => throw new NotSupportedException();
        public Task RebuildProjectionAsync(Type projectionType, TimeSpan shardTimeout, CancellationToken token) => throw new NotSupportedException();
        public Task RebuildProjectionAsync(string projectionName, TimeSpan shardTimeout, CancellationToken token) => throw new NotSupportedException();
        public Task RebuildProjectionAsync<TView>(TimeSpan shardTimeout, CancellationToken token) => throw new NotSupportedException();
        public Task<ISubscriptionAgent> StartAgentAsync(ShardName name, CancellationToken token) => throw new NotSupportedException();
        public Task StopAgentAsync(ShardName shardName, Exception? ex = null) => throw new NotSupportedException();
        public Task CatchUpAsync(CancellationToken cancellation) => throw new NotSupportedException();
        public Task CatchUpAsync(TimeSpan timeout, CancellationToken cancellation) => throw new NotSupportedException();
        public Task WaitForNonStaleData(TimeSpan timeout) => throw new NotSupportedException();
        public long HighWaterMark() => throw new NotSupportedException();
        public DateTimeOffset? HighWaterLastPolledAt => throw new NotSupportedException();
        public bool IsHighWaterStale => throw new NotSupportedException();
        public Task RestartHighWaterAgentAsync(CancellationToken token) => throw new NotSupportedException();
        public Task WaitForShardToBeRunning(string shardName, TimeSpan timeout) => throw new NotSupportedException();
        public Task RewindSubscriptionAsync(string subscriptionName, CancellationToken token, long? sequenceFloor = 0, DateTimeOffset? timestamp = null) => throw new NotSupportedException();
    }
}
