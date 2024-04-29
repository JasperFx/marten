#nullable enable
using System.Diagnostics;

namespace Marten.Internal.OpenTelemetry;

internal static class MartenTracing
{
    // See https://opentelemetry.io/docs/reference/specification/trace/semantic_conventions/messaging/ for more information
    public const string MartenTenantId = "marten.tenant.id";

    internal static ActivitySource ActivitySource { get; } = new(
        "Marten",
        typeof(MartenTracing).Assembly.GetName().Version!.ToString());

    public static Activity? StartConnectionActivity(Activity? parentActivity = null, ActivityTagsCollection? tags = null)
    {
        return StartActivity("connection", parentActivity, tags);
    }

    public static Activity? StartActivity(string spanName, Activity? parentActivity = null, ActivityTagsCollection? tags = null, ActivityKind activityKind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(spanName, activityKind, parentActivity?.ParentId, tags);
    }
}
