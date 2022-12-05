using Marten.Diagnostics.Events;
using System.Diagnostics;

namespace Marten.OpenTelemetry.Internal;

internal static class StreamExtensions
{
    public static void AddStreamEvents(this Activity activity, StreamBaseDiagnosticEvent @event)
    {
        foreach (var evt in @event.StreamAction.Events)
        {
            activity.AddEvent(new(
                evt.EventTypeName,
                evt.Timestamp,
                new ActivityTagsCollection(new Dictionary<string, object?>()
                {
                    {"event_id", evt.Id},
                    {"causation_id", evt.CausationId??string.Empty},
                    { "dotnet_type_name",evt.DotNetTypeName},
                    { "version",evt.Version},
                    { "sequence",evt.Sequence},
                })));
        }
    }
    public static void AddStreamTags(this Activity activity, StreamBaseDiagnosticEvent @event)
    {
        activity.SetTag("correlation_id", @event.CorrelationId ?? string.Empty);
        activity.SetTag("stream_id", @event.StreamAction.Id);
        activity.SetTag("action_type", @event.StreamAction.ActionType);
        activity.SetTag("tenant_id", @event.StreamAction.TenantId?.ToString() ?? string.Empty);
    }
}
