using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

public sealed class ValidateInput: NetCoreInput
{
    [Description("Baseline JSON file path. If the file exists, captured state is diffed against it. If it doesn't exist, captured state is written there and treated as the new baseline.")]
    public string BaselineFlag { get; set; } = "scaletest-baseline.json";

    [Description("Force-overwrite the baseline file with the current capture, no diff. Useful after intentional projection changes.")]
    public bool WriteBaselineFlag { get; set; }
}
