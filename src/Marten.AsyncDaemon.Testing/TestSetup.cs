using Marten.Services.Json;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Marten.AsyncDaemon.TestSetup", "Marten.AsyncDaemon")]

namespace Marten.AsyncDaemon.Testing
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
