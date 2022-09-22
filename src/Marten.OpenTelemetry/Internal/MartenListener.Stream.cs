using Marten.Diagnostics.Events;
using System.Diagnostics;

namespace Marten.OpenTelemetry.Internal;

internal partial class MartenListener
{
    private void StreamCreated(object? payload)
    {
        StreamBaseDiagnosticEvent? @event = payload switch
        {
            StreamCreatedDiagnosticEvent => payload as StreamCreatedDiagnosticEvent,
            StreamAppendedDiagnosticEvent => payload as StreamAppendedDiagnosticEvent,
            _ => null
        };

        if (@event == null)
            return;

        var parent = Activity.Current!.GetCustomProperty("parent") as Activity
            ?? null;

        if (parent == null && @event.CorrelationId != null)
        {
            var corr = ActivitySource.StartActivity("Correlation", ActivityKind.Internal);
            if (corr == null)
                return;

            corr!.SetCustomProperty("parent", corr);
            parent = corr;
        }

        if (parent != null)
        {
            Activity.Current = parent;
            parent.SetCustomProperty("stop_parent", true);
        }
        else
        {
            parent = Activity.Current;
            parent.SetCustomProperty("parent", parent);
        }

        var activity = ActivitySource.StartActivity(@event.EventId.Name!, ActivityKind.Internal);
        if (activity != null)
        {
            activity.DisplayName = @event.DisplayName!;
            activity.AddStreamTags(@event);
            activity.SetCustomProperty("parent", parent);

            var childs = parent.GetCustomProperty("childs_count") as int? ?? 0;
            parent.SetCustomProperty(@event.StreamAction.Id.ToString(), activity);
            parent.SetCustomProperty("childs_count", ++childs);
        }
    }
    private void StreamChangesFinished(object? payload)
    {
        StreamBaseDiagnosticEvent? @event = payload switch
        {
            StreamChangesCompletedDiagnosticEvent => payload as StreamChangesCompletedDiagnosticEvent,
            StreamChangesFailedDiagnosticEvent => payload as StreamChangesFailedDiagnosticEvent,
            _ => null
        };

        if (@event == null)
            return;

        if (Activity.Current?.GetCustomProperty("parent") is Activity parent)
        {
            var activity = parent.GetCustomProperty(@event.StreamAction.Id.ToString()) as Activity;
            activity?.SetEndTime(DateTime.UtcNow);
            activity?.SetTag("aggregate_type", @event.StreamAction.AggregateTypeName);
            activity?.SetTag("expected_version_onserver", @event.StreamAction.ExpectedVersionOnServer?.ToString() ?? string.Empty);
            activity?.AddStreamEvents(@event);
            activity?.Stop();

            var stopParent = parent.GetCustomProperty("stop_parent") as bool?;
            if (stopParent != true)
                return;

            var done = parent.GetCustomProperty("childs_done_count") as int? ?? 0;
            var childs = parent.GetCustomProperty("childs_count") as int? ?? 0;
            parent.SetCustomProperty("childs_done_count", ++done);
            if (done == childs)
                parent.Stop();
        }
    }
}
