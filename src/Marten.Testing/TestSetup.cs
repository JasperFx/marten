using Marten.Services.Json;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Marten.Testing.TestSetup", "Marten.Testing")]

namespace Marten.Testing
{
    public class TestSetup : XunitTestFramework
    {
        public TestSetup(IMessageSink messageSink)
            :base(messageSink)
        {
            SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;
        }
    }
}
