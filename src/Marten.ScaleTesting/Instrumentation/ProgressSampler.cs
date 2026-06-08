using System.Globalization;
using Npgsql;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Background poll loop against <c>mt_event_progression</c>. Every
/// <see cref="InstrumentationOptions.ProgressSampleInterval"/> ticks we read the watched row's
/// <c>last_seq_id</c>, compute the delta since the last sample, and emit a <see cref="ProgressSample"/>.
/// CompositeReplayExecutor batches progression writes ahead of actual scan position, so the
/// per-sample rate is a useful aggregate signal (mean/p50/p95) even though it isn't true
/// per-batch wall-clock -- that needs JasperFx-side hooks (#4684 follow-ups).
/// </summary>
internal sealed class ProgressSampler : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly string _sqlText;
    private readonly TimeSpan _interval;
    private readonly NpgsqlCommandCounter _commandCounter;
    private readonly string? _tracePath;
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;
    private readonly List<ProgressSample> _samples = new();
    private readonly StreamWriter? _traceWriter;
    private long _previousSequence;
    private long _previousCommandCount;
    private DateTimeOffset _previousAt;

    private ProgressSampler(string connectionString, string schema, string progressionRowName,
        TimeSpan interval, NpgsqlCommandCounter commandCounter, string? tracePath,
        CancellationToken outerCancellation)
    {
        _connectionString = connectionString;
        _sqlText = $"select coalesce(last_seq_id, 0) from {schema}.mt_event_progression where name = '{progressionRowName.Replace("'", "''")}'";
        _interval = interval;
        _commandCounter = commandCounter;
        _tracePath = tracePath;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (tracePath != null)
        {
            _traceWriter = new StreamWriter(tracePath, append: false);
            _traceWriter.WriteLine("timestamp,sequence_id,delta_sequence_id,events_per_second,npgsql_commands_in_interval");
        }
        _previousAt = DateTimeOffset.UtcNow;
        _loop = Task.Run(LoopAsync, CancellationToken.None);
    }

    public static ProgressSampler Start(
        string connectionString, string schema, string progressionRowName,
        TimeSpan interval, NpgsqlCommandCounter commandCounter, string? tracePath,
        CancellationToken cancellation)
        => new(connectionString, schema, progressionRowName, interval, commandCounter, tracePath, cancellation);

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
                    // Sampling is best-effort -- a transient connection blip should not crash the
                    // rebuild. Drop the sample and try again next tick.
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
            // Final tick at shutdown so the last partial interval is captured before we stop.
            try { await SampleOnceAsync().ConfigureAwait(false); } catch { /* best effort */ }
        }
    }

    private async Task SampleOnceAsync()
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(_cts.Token).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(_sqlText, conn);
        var current = (long?)await cmd.ExecuteScalarAsync(_cts.Token).ConfigureAwait(false) ?? 0;
        var now = DateTimeOffset.UtcNow;

        var deltaSeq = current - _previousSequence;
        var elapsed = (now - _previousAt).TotalSeconds;
        var rate = elapsed > 0 ? deltaSeq / elapsed : 0;

        var commandSnapshot = _commandCounter.SnapshotCount();
        var commandsInInterval = commandSnapshot - _previousCommandCount;

        var sample = new ProgressSample(
            Timestamp: now,
            SequenceId: current,
            DeltaSequenceId: deltaSeq,
            EventsPerSecondSinceLastSample: rate,
            NpgsqlCommandsSinceLastSample: commandsInInterval);
        lock (_samples)
        {
            _samples.Add(sample);
        }

        if (_traceWriter != null)
        {
            // Flush per-line so a kill -9 still leaves a usable partial trace.
            lock (_traceWriter)
            {
                _traceWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:O},{1},{2},{3:F1},{4}",
                    now.UtcDateTime, current, deltaSeq, rate, commandsInInterval));
                _traceWriter.Flush();
            }
        }

        _previousSequence = current;
        _previousCommandCount = commandSnapshot;
        _previousAt = now;
    }

    public async Task<ProgressSample[]> StopAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); } catch { /* shutdown */ }
        _traceWriter?.Dispose();
        lock (_samples)
        {
            return _samples.ToArray();
        }
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
