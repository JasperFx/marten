namespace Marten.ScaleTesting.Instrumentation;

/// <summary>
/// Toggles for the harness-side instrumentation surface (#4684 Phase E.1). Built behind a
/// `--instrument` flag so production-style runs don't pay for the measurement; the no-op
/// implementations on <see cref="RebuildInstrumentation"/> return zero-allocation null
/// shapes when <see cref="Enabled"/> is false.
/// </summary>
public sealed class InstrumentationOptions
{
    /// <summary>Master toggle. False ⇒ all sampling / counting is a no-op.</summary>
    public bool Enabled { get; init; }

    /// <summary>Periodic poll of <c>mt_event_progression</c> to compute rebuild throughput
    /// samples between intervals. Default 1s; reduce for finer-grained percentiles, raise
    /// to keep overhead negligible on very long runs.</summary>
    public TimeSpan ProgressSampleInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Optional CSV trace path. One row per progress sample: timestamp, sequence,
    /// delta-sequence, instantaneous-events-per-second, npgsql-commands-since-last-sample.
    /// Set via <c>--instrument-trace &lt;path&gt;</c>. When null, only the rolled-up JSON
    /// metrics are written.</summary>
    public string? TracePath { get; init; }

    /// <summary>Optional CSV trace path for the per-sample progression-lock-wait series
    /// (#4684 Phase E.2). One row per sample: timestamp, current waiter count on
    /// <c>mt_event_progression</c>, max single-waiter wait in ms. Set via
    /// <c>--instrument-lock-trace &lt;path&gt;</c>; when null, only the rolled-up stats
    /// land in <c>metrics.json</c>.</summary>
    public string? LockTracePath { get; init; }

    /// <summary>Optional CSV trace path for the per-batch breakdown (#4684 Phase E.3). One row
    /// per completed event page: shard, floor/ceiling, event count, loading/grouping/execution
    /// wall-clock and DB round-trip count. Set via <c>--instrument-batch-trace &lt;path&gt;</c>;
    /// when null, only the p50/p95/p99 roll-up lands in <c>metrics.json</c>.</summary>
    public string? BatchTracePath { get; init; }
}
