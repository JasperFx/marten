using System.Globalization;
using Npgsql;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Background poll loop that snapshots <c>pg_stat_activity</c> + <c>pg_locks</c> to estimate
/// row-level lock contention on <c>mt_event_progression</c> during a rebuild (#4684 Phase E.2,
/// item 5 of the issue). PostgreSQL doesn't expose cumulative per-row lock-wait history --
/// <c>pg_stat_activity</c> shows the *current* wait of each session -- so we approximate by
/// sampling at <see cref="InstrumentationOptions.ProgressSampleInterval"/> and treating each
/// observed waiter as having waited for roughly the interval. Sum across samples gives an
/// observed-waiter-seconds estimate; max across samples gives peak contention.
///
/// Filters out the sampler's own session via <c>pid != pg_backend_pid()</c> so we don't see
/// ourselves; only sessions in <c>state = 'active'</c> with a non-null
/// <c>wait_event_type</c> are counted, and only when they hold an *ungranted* lock on the
/// <c>mt_event_progression</c> relation (joined through <c>pg_locks</c>).
/// </summary>
internal sealed class ProgressionLockSampler : IAsyncDisposable
{
    private const string SqlText = @"
SELECT
    count(*) AS waiter_count,
    coalesce(max(extract(epoch from (now() - a.state_change)) * 1000), 0)::bigint AS max_wait_ms
FROM pg_stat_activity a
WHERE a.state = 'active'
  AND a.wait_event_type IS NOT NULL
  AND a.pid != pg_backend_pid()
  AND EXISTS (
      SELECT 1 FROM pg_locks l
      JOIN pg_class c ON c.oid = l.relation
      WHERE l.pid = a.pid
        AND c.relname = 'mt_event_progression'
        AND l.granted = false
  );";

    private readonly string _connectionString;
    private readonly TimeSpan _interval;
    private readonly string? _tracePath;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly List<LockSample> _samples = new();
    private readonly StreamWriter? _traceWriter;

    private ProgressionLockSampler(string connectionString, TimeSpan interval,
        string? tracePath, CancellationToken outerCancellation)
    {
        _connectionString = connectionString;
        _interval = interval;
        _tracePath = tracePath;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (tracePath != null)
        {
            _traceWriter = new StreamWriter(tracePath, append: false);
            _traceWriter.WriteLine("timestamp,waiter_count,max_wait_ms");
        }
        _loop = Task.Run(LoopAsync, CancellationToken.None);
    }

    public static ProgressionLockSampler Start(string connectionString, TimeSpan interval,
        string? tracePath, CancellationToken cancellation)
        => new(connectionString, interval, tracePath, cancellation);

    private async Task LoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await SampleOnceAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Best effort -- a transient blip should not crash the rebuild. Sampling missed
                    // ticks would slightly under-report; safer than crashing.
                }

                try
                {
                    await Task.Delay(_interval, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            try { await SampleOnceAsync().ConfigureAwait(false); } catch { /* best effort */ }
        }
    }

    private async Task SampleOnceAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(_cts.Token).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(SqlText, conn);
        await using var reader = await cmd.ExecuteReaderAsync(_cts.Token).ConfigureAwait(false);

        var waiterCount = 0L;
        var maxWaitMs = 0L;
        if (await reader.ReadAsync(_cts.Token).ConfigureAwait(false))
        {
            waiterCount = await reader.GetFieldValueAsync<long>(0, _cts.Token).ConfigureAwait(false);
            maxWaitMs = await reader.GetFieldValueAsync<long>(1, _cts.Token).ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        var sample = new LockSample(now, waiterCount, maxWaitMs);
        lock (_samples)
        {
            _samples.Add(sample);
        }

        if (_traceWriter != null)
        {
            lock (_traceWriter)
            {
                _traceWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:O},{1},{2}", now.UtcDateTime, waiterCount, maxWaitMs));
                _traceWriter.Flush();
            }
        }
    }

    public async Task<LockWaitStats> StopAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { /* shutdown */ }
        _traceWriter?.Dispose();
        LockSample[] samples;
        lock (_samples)
        {
            samples = _samples.ToArray();
        }
        return LockWaitStats.From(samples, _interval);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
        }
        _cts.Dispose();
    }
}

/// <summary>One snapshot of progression lock contention.</summary>
internal sealed record LockSample(DateTimeOffset Timestamp, long WaiterCount, long MaxWaitMs);

/// <summary>
/// Rolled-up lock contention stats across a rebuild. <see cref="ObservedWaiterSeconds"/> is the
/// approximation: <c>sum(waiter_count_at_sample) * sample_interval_seconds</c>. A large value
/// signals frequent or long contention; zero means no UPDATE on the progression row ever waited
/// on a lock during the run (the common case once concurrent-rebuild caps are tuned).
/// </summary>
public sealed record LockWaitStats(
    int SampleCount,
    long MaxConcurrentWaiters,
    long MaxSingleWaitMs,
    double ObservedWaiterSeconds)
{
    public static LockWaitStats Empty { get; } = new(0, 0, 0, 0);

    internal static LockWaitStats From(IReadOnlyList<LockSample> samples, TimeSpan interval)
    {
        if (samples.Count == 0)
        {
            return Empty;
        }

        long maxWaiters = 0;
        long maxWaitMs = 0;
        long totalWaiterTicks = 0; // sum of waiter_count, weighted later by interval seconds
        foreach (var s in samples)
        {
            if (s.WaiterCount > maxWaiters) maxWaiters = s.WaiterCount;
            if (s.MaxWaitMs > maxWaitMs) maxWaitMs = s.MaxWaitMs;
            totalWaiterTicks += s.WaiterCount;
        }
        var observedWaiterSeconds = totalWaiterTicks * interval.TotalSeconds;
        return new LockWaitStats(samples.Count, maxWaiters, maxWaitMs, observedWaiterSeconds);
    }
}
