#nullable enable
using System.Diagnostics;
using Marten.Internal.Sessions;
using Marten.Services;
using NSubstitute;
using Shouldly;
using Xunit;

namespace CoreTests.Internal.Sessions;

/// <summary>
/// #5459. <c>MartenTracing.StartActivity</c> passed <c>parentActivity?.ParentId</c> to
/// <c>ActivitySource.StartActivity</c>, so a Marten span was parented to the parent OF the current
/// activity — its grandparent — rather than to the current activity itself.
///
/// The defect is invisible in the two easy cases, which is why it survived:
/// <list type="bullet">
///   <item>No ambient activity: <c>ParentId</c> is null, the null parent id makes the span a root,
///   which is correct.</item>
///   <item>The ambient activity is itself a root: <c>ParentId</c> is still null, and a null parent id
///   makes <c>ActivitySource</c> fall back to <c>Activity.Current</c> — which happens to be the right
///   answer.</item>
/// </list>
/// It only shows up once <c>Activity.Current</c> has a parent of its own — the reported shape, where
/// an incoming Server/Consumer span carries a remote parent from the sending service, and every
/// Marten span underneath it re-parents to that remote span.
/// </summary>
[Collection(nameof(EventTracingConnectionLifetimeCollection))]
public class marten_activity_parenting
{
    private const string DefaultTenant = "default";

    private static readonly ActivitySource TestSource = new("Marten.Testing.Bug5459");

    private static ActivityListener ListenerCapturing(out Box<Activity?> martenConnection)
    {
        var box = new Box<Activity?>();
        martenConnection = box;

        return new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Marten" or "Marten.Testing.Bug5459",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (activity.DisplayName == "marten.connection")
                {
                    box.Value = activity;
                }
            }
        };
    }

    private static void StartAndDisposeATracedConnection()
    {
        var inner = Substitute.For<IConnectionLifetime>();
        using var lifetime = new EventTracingConnectionLifetime(inner, DefaultTenant, new OpenTelemetryOptions());
    }

    [Fact]
    public void marten_span_parents_to_the_current_activity_when_that_activity_has_a_remote_parent()
    {
        using var listener = ListenerCapturing(out var martenConnection);
        ActivitySource.AddActivityListener(listener);

        // Stands in for the Send/Publish span from the calling service...
        using var remoteParent = TestSource.StartActivity("producer.send", ActivityKind.Producer);
        remoteParent.ShouldNotBeNull();

        // ...and this is the incoming Server/Consumer span that a Marten span should hang from.
        using var incoming = TestSource.StartActivity("consumer.receive", ActivityKind.Consumer);
        incoming.ShouldNotBeNull();
        incoming.Parent.ShouldBe(remoteParent);
        Activity.Current.ShouldBe(incoming);

        StartAndDisposeATracedConnection();

        martenConnection.Value.ShouldNotBeNull();

        // The heart of #5459: before the fix this was remoteParent.SpanId.
        martenConnection.Value.ParentSpanId.ShouldBe(incoming.SpanId);
        martenConnection.Value.ParentSpanId.ShouldNotBe(remoteParent.SpanId);
        martenConnection.Value.TraceId.ShouldBe(incoming.TraceId);
    }

    [Fact]
    public void marten_span_parents_to_the_current_activity_when_that_activity_is_a_root()
    {
        using var listener = ListenerCapturing(out var martenConnection);
        ActivitySource.AddActivityListener(listener);

        using var root = TestSource.StartActivity("root.only", ActivityKind.Server);
        root.ShouldNotBeNull();
        root.Parent.ShouldBeNull();

        StartAndDisposeATracedConnection();

        martenConnection.Value.ShouldNotBeNull();
        martenConnection.Value.ParentSpanId.ShouldBe(root.SpanId);
        martenConnection.Value.TraceId.ShouldBe(root.TraceId);
    }

    [Fact]
    public void marten_span_is_a_root_when_there_is_no_ambient_activity()
    {
        using var listener = ListenerCapturing(out var martenConnection);
        ActivitySource.AddActivityListener(listener);

        var previous = Activity.Current;
        Activity.Current = null;

        try
        {
            StartAndDisposeATracedConnection();
        }
        finally
        {
            Activity.Current = previous;
        }

        martenConnection.Value.ShouldNotBeNull();
        martenConnection.Value.Parent.ShouldBeNull();
        martenConnection.Value.ParentSpanId.ShouldBe(default(ActivitySpanId));
    }

    /// <summary>
    /// Three deep, to pin that the span attaches to its immediate parent rather than to any other
    /// rung of the chain — a fix that reached for the root would still pass the two-level test.
    /// </summary>
    [Fact]
    public void marten_span_parents_to_the_nearest_activity_not_an_ancestor()
    {
        using var listener = ListenerCapturing(out var martenConnection);
        ActivitySource.AddActivityListener(listener);

        using var root = TestSource.StartActivity("root", ActivityKind.Producer);
        root.ShouldNotBeNull();
        using var middle = TestSource.StartActivity("middle", ActivityKind.Consumer);
        middle.ShouldNotBeNull();
        using var nearest = TestSource.StartActivity("nearest", ActivityKind.Internal);
        nearest.ShouldNotBeNull();

        StartAndDisposeATracedConnection();

        martenConnection.Value.ShouldNotBeNull();
        martenConnection.Value.ParentSpanId.ShouldBe(nearest.SpanId);
        martenConnection.Value.ParentSpanId.ShouldNotBe(middle.SpanId);
        martenConnection.Value.ParentSpanId.ShouldNotBe(root.SpanId);
    }

    private sealed class Box<T>
    {
        public T? Value { get; set; }
    }
}
