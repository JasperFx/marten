using System;
using System.Reflection;
using Bobcat;
using Bobcat.Monitoring;
using Xunit.v3;

namespace DaemonTests.TestingSupport;

/// <summary>
/// Opens a Bobcat scenario around each test, so calls to <c>[BobcatStep]</c> helpers on
/// <see cref="DaemonContext"/> report themselves as steps, and publishes the verdict to a running
/// Bobcat console (JasperFx/bobcat#110).
/// </summary>
/// <remarks>
/// The runner-specific half of the marker-step style. Bobcat's own attributes stay runner-neutral
/// — the generator matches test methods by attribute NAME and references no runner — so this small
/// adapter is what a shipped Bobcat.Xunit package would contain.
///
/// Inert when nothing is listening: with no console on the wire the publisher is null, no events
/// are sent, and the only cost is a few strings per test.
/// </remarks>
public sealed class BobcatScenarioAttribute : BeforeAfterTestAttribute
{
    internal static readonly Guid RunId = Guid.NewGuid();

    private static readonly Lazy<IMonitorEventSink?> Sink = new(() =>
    {
        var publisher = MonitorPublisher.TryConnect().GetAwaiter().GetResult();

        publisher?.Post(new RunStarted(
            RunId, "DaemonTests", Repository: "", Branch: null, Mode: "xunit-marker-steps",
            StartedAt: DateTimeOffset.UtcNow, TotalScenarios: null));

        return publisher;
    });

    private ScenarioRecorder.Recording? _recording;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var feature = methodUnderTest.DeclaringType?.GetCustomAttribute<BobcatFeatureAttribute>()?.Title
                      ?? Prettify(methodUnderTest.DeclaringType?.Name ?? "DaemonTests");

        _recording = ScenarioRecorder.Begin(feature, Prettify(methodUnderTest.Name), Sink.Value, RunId);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (_recording is null) return;

        Console.WriteLine($"  {_recording.Uid}");
        foreach (var step in _recording.Steps)
        {
            Console.WriteLine($"    {step}  ({step.DurationMs}ms)");
        }

        _recording.Dispose();
        _recording = null;
    }

    private static string Prettify(string name) => name.Replace('_', ' ');
}
