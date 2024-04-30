#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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

    public void ExportCounterOnChangeSets<T>(string name, string units, Action<Counter<T>, IChangeSet> recordAction) where T : struct
    {
        var counter = Meter.CreateCounter<T>(name, units);
        Applications.Add(commit =>
        {
            recordAction(counter, commit);
        });
    }

    public Meter Meter { get; } = new Meter("Marten");

}

internal class MartenCommitMetrics(ILogger logger, List<Action<IChangeSet>> applications): DocumentSessionListenerBase
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
                Debug.WriteLine("Metrics gathering failure");
                Debug.WriteLine(e.ToString());
            }
        }
    }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        AfterCommit(session, commit);
        return Task.CompletedTask;
    }
}
