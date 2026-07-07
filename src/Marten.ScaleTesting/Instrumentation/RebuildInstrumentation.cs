using System.Diagnostics;

namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Orchestrates the harness-side instrumentation for a single rebuild (#4684 Phase E.1).
/// Owns the <see cref="ProgressSampler"/> background poller against
/// <c>mt_event_progression</c>, the <see cref="NpgsqlCommandCounter"/> DiagnosticSource
/// subscription, and the percentile aggregation that lands in <c>metrics.json</c>.
///
/// All methods are safe to call when <see cref="InstrumentationOptions.Enabled"/> is false --
/// they are no-ops that allocate nothing and return the disabled-shape <see cref="Snapshot"/>.
/// </summary>
public sealed class RebuildInstrumentation : IAsyncDisposable
{
    private readonly InstrumentationOptions _options;
    private readonly ProgressSampler? _sampler;
    private readonly NpgsqlCommandCounter? _commands;
    private readonly ProgressionLockSampler? _lockSampler;
    private readonly BatchSpanSampler? _batchSampler;
    private readonly Activity? _activity;
    private static readonly ActivitySource s_activitySource = new("Marten.ScaleTesting", "1.0");

    private RebuildInstrumentation(InstrumentationOptions options, ProgressSampler? sampler,
        NpgsqlCommandCounter? commands, ProgressionLockSampler? lockSampler,
        BatchSpanSampler? batchSampler, Activity? activity)
    {
        _options = options;
        _sampler = sampler;
        _commands = commands;
        _lockSampler = lockSampler;
        _batchSampler = batchSampler;
        _activity = activity;
    }

    /// <summary>
    /// Spin up an instrumented rebuild scope. When <paramref name="options"/>.Enabled is false
    /// the returned object is a no-op (no background sampler, no DiagnosticSource subscription,
    /// no Activity).
    /// </summary>
    public static RebuildInstrumentation Start(
        InstrumentationOptions options,
        string connectionString,
        string schemaName,
        string progressionRowName,
        CancellationToken cancellation)
    {
        if (!options.Enabled)
        {
            return new RebuildInstrumentation(options, null, null, null, null, null);
        }

        var activity = s_activitySource.StartActivity("scaletest.rebuild", ActivityKind.Internal);
        activity?.SetTag("progression.name", progressionRowName);
        activity?.SetTag("schema", schemaName);

        var commands = NpgsqlCommandCounter.Start();
        var sampler = ProgressSampler.Start(
            connectionString, schemaName, progressionRowName, options.ProgressSampleInterval,
            commands, options.TracePath, cancellation);
        var lockSampler = ProgressionLockSampler.Start(
            connectionString, options.ProgressSampleInterval, options.LockTracePath, cancellation);

        // E.3: listening to the Marten source is what turns the daemon's per-page spans on
        var batchSampler = BatchSpanSampler.Start();
        LookupCounters.Reset();
        LookupCounters.Enabled = true;

        return new RebuildInstrumentation(options, sampler, commands, lockSampler, batchSampler, activity);
    }

    /// <summary>
    /// Capture the final snapshot. Stops the sampler / counter / activity. Idempotent.
    /// </summary>
    public async Task<Snapshot> CaptureAsync()
    {
        if (!_options.Enabled)
        {
            return Snapshot.Disabled;
        }

        var samples = _sampler != null ? await _sampler.StopAsync().ConfigureAwait(false) : Array.Empty<ProgressSample>();
        var commandCount = _commands?.Stop() ?? 0;
        var lockWait = _lockSampler != null ? await _lockSampler.StopAsync().ConfigureAwait(false) : LockWaitStats.Empty;

        // E.3: roll the per-page spans up into per-batch breakdown + lookup profile
        var batchRecords = _batchSampler?.Capture(_options.BatchTracePath) ?? Array.Empty<BatchRecord>();
        _batchSampler?.Dispose();
        LookupCounters.Enabled = false;
        var lookups = LookupCounters.Capture();
        LookupCounters.Reset();

        _activity?.SetTag("samples", samples.Length);
        _activity?.SetTag("npgsql.commands", commandCount);
        _activity?.SetTag("progression.max_waiters", lockWait.MaxConcurrentWaiters);
        _activity?.SetTag("progression.observed_waiter_seconds", lockWait.ObservedWaiterSeconds);
        _activity?.SetTag("batches", batchRecords.Count);
        _activity?.Stop();

        return new Snapshot(
            Enabled: true,
            Samples: samples,
            NpgsqlCommandCount: commandCount,
            Throughput: ThroughputPercentiles.From(samples),
            ProgressionLockWaits: lockWait,
            PerBatch: BatchBreakdownStats.From(batchRecords),
            Lookups: lookups);
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent disposal so the using/await-using pattern works even when CaptureAsync was
        // already called explicitly by the command.
        if (_sampler != null)
        {
            await _sampler.DisposeAsync().ConfigureAwait(false);
        }
        if (_lockSampler != null)
        {
            await _lockSampler.DisposeAsync().ConfigureAwait(false);
        }
        _commands?.Dispose();
        _batchSampler?.Dispose();
        LookupCounters.Enabled = false;
        _activity?.Dispose();
    }

    /// <summary>Result shape emitted into <c>metrics.json</c> under <c>"instrumentation"</c>.</summary>
    public sealed record Snapshot(
        bool Enabled,
        IReadOnlyList<ProgressSample> Samples,
        long NpgsqlCommandCount,
        ThroughputPercentiles Throughput,
        LockWaitStats ProgressionLockWaits,
        BatchBreakdownStats PerBatch,
        IReadOnlyDictionary<string, LookupStats> Lookups)
    {
        public static Snapshot Disabled { get; } =
            new(false, Array.Empty<ProgressSample>(), 0, ThroughputPercentiles.Empty, LockWaitStats.Empty,
                BatchBreakdownStats.Empty, new Dictionary<string, LookupStats>());
    }
}

/// <summary>
/// One progress sample: where progression sat at a wall-clock moment, plus the delta-events-per-second
/// since the previous sample. <see cref="NpgsqlCommandsSinceLastSample"/> reads the
/// <see cref="NpgsqlCommandCounter"/> at sample time, so a per-sample throughput row also carries the
/// matching round-trip count.
/// </summary>
public sealed record ProgressSample(
    DateTimeOffset Timestamp,
    long SequenceId,
    long DeltaSequenceId,
    double EventsPerSecondSinceLastSample,
    long NpgsqlCommandsSinceLastSample);

/// <summary>
/// Throughput percentiles computed from a finished rebuild's <see cref="ProgressSample"/> series.
/// Excludes the cold-start sample (delta is total processed at first poll) so the p50/p95/p99
/// reflect steady-state rather than the first-tick artifact.
/// </summary>
public sealed record ThroughputPercentiles(double P50, double P95, double P99, double Mean, double Max)
{
    public static ThroughputPercentiles Empty { get; } = new(0, 0, 0, 0, 0);

    public static ThroughputPercentiles From(IReadOnlyList<ProgressSample> samples)
    {
        if (samples.Count < 2)
        {
            return Empty;
        }

        // Drop the first sample -- its delta is the whole "from zero to first poll" total, not a
        // representative steady-state tick.
        var rates = samples
            .Skip(1)
            .Where(s => s.EventsPerSecondSinceLastSample > 0)
            .Select(s => s.EventsPerSecondSinceLastSample)
            .OrderBy(r => r)
            .ToArray();

        if (rates.Length == 0)
        {
            return Empty;
        }

        return new ThroughputPercentiles(
            P50: Percentile(rates, 0.50),
            P95: Percentile(rates, 0.95),
            P99: Percentile(rates, 0.99),
            Mean: rates.Average(),
            Max: rates[^1]);
    }

    private static double Percentile(IReadOnlyList<double> sorted, double q)
    {
        // Nearest-rank percentile -- a small-N-friendly definition that doesn't interpolate. For
        // the harness rate-sample volumes (a few thousand at most) interpolation noise isn't worth
        // the algorithmic complexity.
        var rank = (int)Math.Ceiling(q * sorted.Count);
        var idx = Math.Clamp(rank - 1, 0, sorted.Count - 1);
        return sorted[idx];
    }
}
