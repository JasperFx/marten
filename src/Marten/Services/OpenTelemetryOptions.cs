#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.OpenTelemetry;
using Microsoft.Extensions.Logging;

namespace Marten.Services;

public enum TrackLevel
{
    /// <summary>
    /// No Open Telemetry tracking
    /// </summary>
    None,

    /// <summary>
    /// Normal level of Open Telemetry tracking
    /// </summary>
    Normal,

    /// <summary>
    /// Very verbose event tracking, only suitable for debugging or performance tuning
    /// </summary>
    Verbose
}

public sealed class OpenTelemetryOptions
{
    /// <summary>
    /// Used to track OpenTelemetry events for opening an connection or exceptions on a connection, for example when a command or data reader has been executed. This defaults to false.
    /// </summary>
    public TrackLevel TrackConnections { get; set; } = TrackLevel.None;

    internal List<Action<IChangeSet>> Applications { get; } = new();

    /// <summary>
    /// Add a custom counter that will be applied after a DocumentSession is committed
    /// </summary>
    /// <param name="name"></param>
    /// <param name="units"></param>
    /// <param name="recordAction"></param>
    /// <typeparam name="T"></typeparam>
    public void ExportCounterOnChangeSets<T>(string name, string units, Action<Counter<T>, IChangeSet> recordAction) where T : struct
    {
        var counter = Meter.CreateCounter<T>(name, units);
        Applications.Add(commit =>
        {
            recordAction(counter, commit);
        });
    }

    /// <summary>
    /// Direct Marten to export counters on the events being appended
    /// </summary>
    public void TrackEventCounters()
    {
        ExportCounterOnChangeSets<long>("marten.event.append", "events", (counter, commit) =>
        {
            foreach (var e in commit.GetEvents())
            {
                counter.Add(1, new TagList
                {
                    { MartenTracing.EventType, e.EventTypeName },
                    { MartenTracing.TenantId, e.TenantId }
                });
            }
        });
    }

    public Meter Meter { get; } = new("Marten");

}

internal class MartenCommitMetrics(ILogger Logger, List<Action<IChangeSet>> applications): DocumentSessionListenerBase
{
    public List<Action<IChangeSet>> Applications { get; } = applications;

    public override void AfterCommit(IDocumentSession session, IChangeSet commit)
    {
        foreach (var application in Applications)
        {
            try
            {
                application(commit);
            }
            catch (Exception e)
            {
                // Really don't expect this as the metrics should be
                Logger.LogError(e, "Metrics gathering failure");
            }
        }
    }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        AfterCommit(session, commit);
        return Task.CompletedTask;
    }
}
