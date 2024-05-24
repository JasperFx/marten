using Marten.Services.Json;
using Marten.Testing.Harness;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("LinqTests.TestSetup", "LinqTests")]

namespace LinqTests;

public class TestSetup : XunitTestFramework
{
    public TestSetup(IMessageSink messageSink)
        :base(messageSink)
    {
        SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;
    }
}
