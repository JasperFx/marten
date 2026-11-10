#nullable enable
using System.Diagnostics;
using Marten.Services;

namespace Marten.Internal.OpenTelemetry;

/// <summary>
///     Marten's spans start from the one process-wide "Marten" <see cref="ActivitySource" /> that
///     JasperFx's <see cref="JasperFx.OpenTelemetry.OpenTelemetryOptions" /> shares by name (jasperfx#1054).
///     This used to construct its own, so a listener on "Marten" could hear two sources of that name.
/// </summary>
internal static class MartenTracing
{
    private static readonly OpenTelemetryOptions Shared = new();

    internal static ActivitySource ActivitySource => Shared.ActivitySource;

    public static Activity? StartConnectionActivity(Activity? parentActivity = null, ActivityTagsCollection? tags = null)
    {
        return StartActivity("marten.connection", parentActivity, tags);
    }

    // #5459: JasperFx's StartActivity parents to the activity it is handed (or Activity.Current), never to
    // that activity's own parent, and passes the ActivityContext so sampling flags and tracestate travel.
    public static Activity? StartActivity(string spanName, Activity? parentActivity = null, ActivityTagsCollection? tags = null, ActivityKind activityKind = ActivityKind.Internal)
        => Shared.StartActivity(spanName, parentActivity, tags, activityKind);
}
