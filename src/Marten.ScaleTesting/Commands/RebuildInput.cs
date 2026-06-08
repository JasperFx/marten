using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

public sealed class RebuildInput: NetCoreInput
{
    [Description("Projection name to rebuild. Defaults to the TelehealthComposite. Pass any registered projection name to scope a narrower rebuild.")]
    public string ProjectionFlag { get; set; } = "TelehealthComposite";

    [Description("Per-shard rebuild timeout, in seconds. Default: 600s (10 min). Bump for very large event counts.")]
    public int ShardTimeoutSecondsFlag { get; set; } = 600;

    [Description("#4684 Phase E.1: enable per-period throughput sampling against mt_event_progression + Npgsql command counting. Adds an Activity span around the rebuild and writes the rolled-up percentiles into the metrics file. Off by default so production-style runs don't pay for the measurement.")]
    public bool InstrumentFlag { get; set; }

    [Description("Optional CSV trace path. Implies --instrument. One row per progress sample: timestamp, sequence_id, delta, events_per_second, npgsql_commands_in_interval.")]
    public string? InstrumentTraceFlag { get; set; }

    [Description("Sample interval for the progression poller when --instrument is on. Default 1s. Lower for finer-grained percentiles; raise to keep harness overhead negligible on multi-hour runs.")]
    public double InstrumentSampleSecondsFlag { get; set; } = 1.0;

    [Description("Metrics JSON output path. When set (and --instrument is on), the instrumentation snapshot is written here as JSON. Default unset = no JSON file emitted; instrumentation is still reported on the console.")]
    public string? MetricsFlag { get; set; }
}
