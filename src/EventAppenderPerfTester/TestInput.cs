using JasperFx;
using JasperFx.CommandLine;

namespace EventAppenderPerfTester;

public class TestInput: NetCoreInput
{
    public TestType TypeFlag { get; set; } = TestType.All;
}
