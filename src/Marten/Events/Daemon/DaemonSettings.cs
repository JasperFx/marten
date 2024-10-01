using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.Resiliency;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Daemon;

public interface IReadOnlyDaemonSettings
{
    /// <summary>
    ///     If the projection daemon detects a "stale" event sequence that is probably cause
    ///     by sequence numbers being reserved, but never committed, this is the threshold to say
    ///     "just look for the highest contiguous sequence number newer than X amount of time" to trigger
    ///     the daemon to continue advancing. The default is 3 seconds.
    /// </summary>
    TimeSpan StaleSequenceThreshold { get; }

    /// <summary>
    ///     Polling time between looking for a new high water sequence mark
    ///     if the daemon detects low activity. The default is 1 second.
    /// </summary>
    TimeSpan SlowPollingTime { get; }

    /// <summary>
    ///     Polling time between looking for a new high water sequence mark
    ///     if the daemon detects high activity. The default is 250ms
    /// </summary>
    TimeSpan FastPollingTime { get; }

    /// <summary>
    ///     Polling time for the running projection daemon to determine the health
    ///     of its activities and try to restart anything that is not currently running
    /// </summary>
    TimeSpan HealthCheckPollingTime { get; }

    /// <summary>
    ///     Projection Daemon mode. The default is Disabled
    /// </summary>
    DaemonMode AsyncMode { get; }
}

public class DaemonSettings: IReadOnlyDaemonSettings
{
    public const int RebuildBatchSize = 1000;

    /// <summary>
    ///     Register session listeners that will ONLY be applied within the asynchronous daemon updates.
    /// </summary>
    public readonly List<IChangeListener> AsyncListeners = new();

    /// <summary>
    ///     This is used to establish a global lock id for the async daemon and should
    ///     be unique for any applications that target the same database.
    /// </summary>
    public int DaemonLockId { get; set; } = 4444;

    /// <summary>
    ///     Time in milliseconds to poll for leadership election in the async projection daemon
    /// </summary>
    public int LeadershipPollingTime { get; set; } = 5000;

    /// <summary>
    ///     If the projection daemon detects a "stale" event sequence that is probably cause
    ///     by sequence numbers being reserved, but never committed, this is the threshold to say
    ///     "just look for the highest contiguous sequence number newer than X amount of time" to trigger
    ///     the daemon to continue advancing. The default is 3 seconds.
    /// </summary>
    public TimeSpan StaleSequenceThreshold { get; set; } = 3.Seconds();

    /// <summary>
    ///     Polling time between looking for a new high water sequence mark
    ///     if the daemon detects low activity. The default is 1 second.
    /// </summary>
    public TimeSpan SlowPollingTime { get; set; } = 1.Seconds();

    /// <summary>
    ///     Polling time between looking for a new high water sequence mark
    ///     if the daemon detects high activity. The default is 250ms
    /// </summary>
    public TimeSpan FastPollingTime { get; set; } = 250.Milliseconds();

    /// <summary>
    ///     Polling time for the running projection daemon to determine the health
    ///     of its activities and try to restart anything that is not currently running
    /// </summary>
    public TimeSpan HealthCheckPollingTime { get; set; } = 5.Seconds();

    /// <summary>
    /// If a subscription has been paused for any reason
    /// </summary>
    public TimeSpan AgentPauseTime { get; set; } = 1.Seconds();

    /// <summary>
    ///     Projection Daemon mode. The default is Disabled. As of V5, the async daemon needs to be
    ///     explicitly added to the system with AddMarten().AddAsyncDaemon();
    /// </summary>
    public DaemonMode AsyncMode { get; internal set; } = DaemonMode.Disabled;
}
