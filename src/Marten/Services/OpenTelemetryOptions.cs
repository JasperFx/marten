#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Descriptors;
using Marten.Internal.OpenTelemetry;
using Microsoft.Extensions.Logging;

namespace Marten.Services;

/// <summary>
///     Marten's OpenTelemetry configuration. The meter, the activity source, the connection tracking level
///     and the change set counters come from JasperFx's <see cref="JasperFx.OpenTelemetry.OpenTelemetryOptions{TChangeSet}" />
///     (jasperfx#1054): one <c>Meter</c> and one <c>ActivitySource</c> named "Marten" for the whole process.
/// </summary>
public sealed class OpenTelemetryOptions: JasperFx.OpenTelemetry.OpenTelemetryOptions<IChangeSet>
{
    public OpenTelemetryOptions(): base("Marten")
    {
    }

    /// <summary>
    /// Tag carrying the .NET type name of the aggregate a fetch was for.
    /// </summary>
    internal const string AggregateTypeTag = "aggregate.type";

    /// <summary>
    /// Tag carrying which fetch plan served the call: Live, Inline or Async.
    /// </summary>
    internal const string FetchPlanTag = "fetch.plan";

    internal const string LivePlan = "Live";
    internal const string InlinePlan = "Inline";
    internal const string AsyncPlan = "Async";

    // Null until the store is built with EnableFetchForWritingMetrics on, so an application that turns these
    // metrics off pays a null check on the fetch path and nothing else.
    private Histogram<long>? _eventsReplayed;
    private bool _eventCountersExported;

    // #6007: created on the first late event, which a healthy store never has
    private Counter<long>? _lateEvents;

    internal const string LateEventsMetricName = "marten.daemon.late_events";
    internal const string SkipReasonTag = "skip.reason";

    /// <summary>
    ///     #6007: one event that committed inside a sequence range the high water mark had already skipped
    /// </summary>
    internal void RecordLateEvent(string eventType, string tenantId, string skipReason)
    {
        var counter = LazyInitializer.EnsureInitialized(ref _lateEvents,
            () => Meter.CreateCounter<long>(LateEventsMetricName, "events",
                "Events that committed inside a sequence range the high water mark had already skipped"));

        if (!counter.Enabled) return;

        counter.Add(1, new TagList
        {
            { OtelConstants.EventType, eventType },
            { OtelConstants.TenantId, tenantId },
            { SkipReasonTag, skipReason }
        });
    }

    /// <summary>
    ///     Export the <c>marten.event.append</c> counter: one count per appended event, tagged by event type
    ///     and tenant id. On by default since 10.0 (#5812); set to <c>false</c> to opt out.
    /// </summary>
    public bool EnableEventCounters { get; set; } = true;

    /// <summary>
    ///     Record the <c>marten.fetch_for_writing.events_replayed</c> histogram: how many events a single
    ///     <c>FetchForWriting()</c> call had to read back and fold to reconstruct the aggregate, tagged by
    ///     aggregate type and by which fetch plan served it. On by default since 10.0 (#5812); set to
    ///     <c>false</c> to opt out.
    /// </summary>
    /// <remarks>
    ///     This is the number behind the cost of the command handler workflow. A <c>Live</c> aggregate
    ///     replays its whole stream on every fetch, an <c>Async</c> one replays only what the daemon has
    ///     not caught up on yet, and an <c>Inline</c> one always replays zero. Recording all three lets you
    ///     see what a lifecycle change would actually buy before making it.
    /// </remarks>
    public bool EnableFetchForWritingMetrics { get; set; } = true;

    /// <summary>
    ///     Opt into the <c>marten.fetch_for_writing.events_replayed</c> histogram. On by default since 10.0, so
    ///     this is the same as setting <see cref="EnableFetchForWritingMetrics" /> to <c>true</c>.
    /// </summary>
    public void TrackFetchForWritingMetrics()
    {
        EnableFetchForWritingMetrics = true;
    }

    /// <summary>
    ///     Called when the store is built, so the two switches above can be set in any order.
    /// </summary>
    internal void ApplyDefaultMetrics()
    {
        if (EnableEventCounters && !_eventCountersExported)
        {
            _eventCountersExported = true;
            exportEventCounters();
        }

        if (EnableFetchForWritingMetrics)
        {
            _eventsReplayed ??= Meter.CreateHistogram<long>(
                "marten.fetch_for_writing.events_replayed",
                "events",
                "Events read back and folded into the aggregate by a single FetchForWriting() call");
        }
    }

    /// <summary>
    ///     Called from the fetch plans. Both tag values are cached per plan instance by the caller, so a
    ///     recorded fetch allocates nothing beyond the stack-based <see cref="TagList" />.
    /// </summary>
    internal void RecordEventsReplayed(int count, string aggregateTypeName, string fetchPlan)
    {
        var histogram = _eventsReplayed;
        if (histogram is null || !histogram.Enabled)
        {
            return;
        }

        histogram.Record(count, new TagList
        {
            { AggregateTypeTag, aggregateTypeName }, { FetchPlanTag, fetchPlan }
        });
    }

    /// <summary>
    /// Direct Marten to export counters on the events being appended. On by default since 10.0, so this is
    /// the same as setting <see cref="EnableEventCounters" /> to <c>true</c>.
    /// </summary>
    public void TrackEventCounters()
    {
        EnableEventCounters = true;
    }

    private void exportEventCounters()
    {
        ExportCounterOnChangeSets<long>("marten.event.append", "events", (counter, commit) =>
        {
            // #5730: nothing is listening to the meter, so do not walk every committed event to build tags
            if (!counter.Enabled) return;

            foreach (var e in commit.GetEvents())
            {
                counter.Add(1, new TagList
                {
                    { OtelConstants.EventType, e.EventTypeName },
                    { OtelConstants.TenantId, e.TenantId }
                });
            }
        });
    }
}

internal class MartenCommitMetrics(ILogger Logger, OpenTelemetryOptions options): DocumentSessionListenerBase
{
    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // Each counter is caught and logged on its own: the commit is already durable here.
        options.RecordCommit(commit, Logger);
        return Task.CompletedTask;
    }
}
