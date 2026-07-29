using System.Runtime.CompilerServices;
using Marten.Services.Json;
using Marten.Testing.Harness;

namespace Marten.Testing;

/// <summary>
/// Pins the serializer this assembly's tests run against.
/// </summary>
/// <remarks>
/// This was an <c>XunitTestFramework</c> subclass wired up with
/// <c>[assembly: TestFramework(...)]</c> under xunit v2. v3 reworked that extensibility
/// point entirely (and its runner-extensibility docs are still marked forthcoming), so
/// a module initializer carries the one assignment instead — it runs before any test
/// class is constructed and does not depend on the test framework at all.
/// </remarks>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;
    }
}
