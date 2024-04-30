#nullable enable
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using JasperFx.Core;
using Marten.Internal.OpenTelemetry;

namespace Marten.Events.Daemon.Internals;

public interface ISubscriptionMetrics
{
    Activity? TrackExecution(EventRange page);
    Activity? TrackLoading(EventRequest request);
    void UpdateGap(long highWaterMark, long lastCeiling);
    void UpdateProcessed(long count);
    Activity? TrackGrouping(EventRange page);
}

public class NulloSubscriptionMetrics: ISubscriptionMetrics
{
    public Activity? TrackExecution(EventRange page)
    {
        return null;
    }

    public Activity? TrackLoading(EventRequest request)
    {
        return null;
    }

    public void UpdateGap(long highWaterMark, long lastCeiling)
    {
    }

    public void UpdateProcessed(long count)
    {
    }

    public Activity? TrackGrouping(EventRange page)
    {
        return null;
    }
}

internal class SubscriptionMetrics: ISubscriptionMetrics
{
    private readonly Meter _meter;
    private readonly string _databaseName;
    private readonly Counter<long> _processed;
    private readonly Histogram<long> _gap;

    public SubscriptionMetrics(Meter meter, ShardName name, string databaseName)
    {
        _meter = meter;
        _databaseName = databaseName;
        Name = name;

        var identifier = $"marten.{name.ProjectionName.ToLower()}.{name.Key.ToLower()}";
        var databaseIdentifier = databaseName.EqualsIgnoreCase("Marten")
            ? identifier
            : $"marten.{databaseName.ToLower()}.{name.ProjectionName.ToLower()}.{name.Key.ToLower()}";


        _processed = meter.CreateCounter<long>(
            $"{identifier}.processed");

        _gap = meter.CreateHistogram<long>($"{databaseIdentifier}.gap");


        ExecutionSpanName = $"{identifier}.page.execution";
        LoadingSpanName = $"{identifier}.page.loading";
        GroupingSpanName = $"{identifier}.page.grouping";
    }

    public string LoadingSpanName { get; }
    public string GroupingSpanName { get; }

    public Activity? TrackExecution(EventRange page)
    {
        var activity = MartenTracing.StartActivity(ExecutionSpanName, activityKind: ActivityKind.Internal);
        activity?.AddTag("page.size", page.Events.Count);
        activity?.AddTag("event.floor", page.SequenceFloor);
        activity?.AddTag("event.ceiling", page.SequenceCeiling);
        activity?.AddTag("marten.database", _databaseName);

        return activity;
    }

    public Activity? TrackGrouping(EventRange page)
    {
        var activity = MartenTracing.StartActivity(GroupingSpanName, activityKind: ActivityKind.Internal);
        activity?.AddTag("page.size", page.Events.Count);
        activity?.AddTag("event.floor", page.SequenceFloor);
        activity?.AddTag("event.ceiling", page.SequenceCeiling);
        activity?.AddTag("marten.database", _databaseName);

        return activity;
    }

    public Activity? TrackLoading(EventRequest request)
    {
        var activity = MartenTracing.StartActivity(LoadingSpanName, activityKind: ActivityKind.Internal);
        activity?.AddTag("event.floor", request.Floor);
        activity?.AddTag("marten.database", _databaseName);

        return activity;
    }

    public void UpdateGap(long highWaterMark, long lastCeiling)
    {
        _gap.Record(highWaterMark - lastCeiling);
    }

    public void UpdateProcessed(long count)
    {
        _processed.Add(count, new TagList
        {
            {"marten.database", _databaseName}
        });
    }

    public string ExecutionSpanName { get; }

    public ShardName Name { get; }
}
