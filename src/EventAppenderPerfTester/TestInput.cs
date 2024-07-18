using Oakton;

namespace EventAppenderPerfTester;

public class TestInput: NetCoreInput
{
    public TestType TypeFlag { get; set; } = TestType.All;
}