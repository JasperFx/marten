using Marten.Services.Json;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Marten.Schema.TestSetup", "Marten.Schema")]

namespace Marten.Schema.Testing
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
