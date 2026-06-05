using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

public sealed class RebuildInput: NetCoreInput
{
    [Description("Projection name to rebuild. Defaults to the TelehealthComposite. Pass any registered projection name to scope a narrower rebuild.")]
    public string ProjectionFlag { get; set; } = "TelehealthComposite";

    [Description("Per-shard rebuild timeout, in seconds. Default: 600s (10 min). Bump for very large event counts.")]
    public int ShardTimeoutSecondsFlag { get; set; } = 600;
}
