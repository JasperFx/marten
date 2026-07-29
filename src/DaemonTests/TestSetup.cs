using System.Runtime.CompilerServices;
using Marten.Services.Json;
using Marten.Testing.Harness;

namespace Marten.AsyncDaemon.Testing;

/// <summary>
/// Pins the serializer this assembly's tests run against.
/// </summary>
/// <remarks>
/// This used to be an <c>XunitTestFramework</c> subclass, but its
/// <c>[assembly: TestFramework(...)]</c> named a type and assembly that stopped existing at
/// some rename, so xunit could never load it and DaemonTests silently ran under
/// System.Text.Json regardless of DEFAULT_SERIALIZER. See #5066.
/// </remarks>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;
    }
}
