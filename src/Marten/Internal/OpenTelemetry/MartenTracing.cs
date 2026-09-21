#nullable enable
using System.Diagnostics;

namespace Marten.Internal.OpenTelemetry;

internal static class MartenTracing
{
    internal static ActivitySource ActivitySource { get; } = new(
        "Marten",
        typeof(MartenTracing).Assembly.GetName().Version!.ToString());

    public static Activity? StartConnectionActivity(Activity? parentActivity = null, ActivityTagsCollection? tags = null)
    {
        return StartActivity("marten.connection", parentActivity, tags);
    }

    public static Activity? StartActivity(string spanName, Activity? parentActivity = null, ActivityTagsCollection? tags = null, ActivityKind activityKind = ActivityKind.Internal)
    {
        // #5459: parent to the activity we were handed, NOT to that activity's own parent.
        // `parentActivity?.ParentId` re-parented every Marten span to its grandparent whenever the
        // ambient activity had a parent -- the shape you get from an incoming Server/Consumer span
        // carrying a remote traceparent, where Marten's spans then hung off the *sending* service's
        // span. It looked correct in the easy cases only by accident: with no ambient activity, or
        // with an ambient root, `ParentId` is null, and a null parent id makes ActivitySource fall
        // back to Activity.Current.
        //
        // Hand over the ActivityContext rather than the id string so the sampling flags and
        // tracestate travel with it. A default context means "no parent", which is exactly what we
        // want when nothing is ambient either.
        var parent = parentActivity ?? Activity.Current;

        return ActivitySource.StartActivity(spanName, activityKind, parent?.Context ?? default, tags);
    }
}
