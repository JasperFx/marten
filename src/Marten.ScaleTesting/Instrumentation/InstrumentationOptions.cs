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
}
