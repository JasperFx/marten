using System;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;

namespace Marten.Testing;

public class ProjectionDaemonRunner(string schemaName, string connectionString, ILogger<IProjection> logger) : OneOffConfigurationsHelper(schemaName, connectionString)
{
    private ProjectionDaemon? _daemon;
    public ILogger<IProjection> Logger { get; } = logger;

    public ProjectionDaemonRunner(string connectionString, ILogger<IProjection> logger)
        : this("daemon", connectionString, logger)
    {
    }

    public async Task<IProjectionDaemon> StartDaemon()
    {
        _daemon = new ProjectionDaemon(TheStore, Logger);
        await _daemon.StartAllShards();
        return _daemon;
    }

    public async Task<IProjectionDaemon> StartDaemon(string tenantId)
    {
        var daemon = (ProjectionDaemon)await TheStore.BuildProjectionDaemonAsync(tenantId, Logger);

        await daemon.StartAllShards();

        _daemon = daemon;

        return daemon;
    }

    public async Task<IProjectionDaemon> StartDaemonInHotColdMode()
    {
        TheStore.Options.Projections.LeadershipPollingTime = 100;

        var coordinator =
            new HotColdCoordinator(TheStore.Tenancy.Default.Database, TheStore.Options.Projections, Logger);
        var daemon = new ProjectionDaemon(TheStore, TheStore.Tenancy.Default.Database,
            new HighWaterDetector(coordinator, TheStore.Events, Logger), Logger);

        await daemon.UseCoordinator(coordinator);

        _daemon = daemon;

        Disposables.Add(daemon);
        return daemon;
    }

    public async Task<IProjectionDaemon> StartAdditionalDaemonInHotColdMode()
    {
        TheStore.Options.Projections.LeadershipPollingTime = 100;
        var coordinator =
            new HotColdCoordinator(TheStore.Tenancy.Default.Database, TheStore.Options.Projections, Logger);
        var daemon = new ProjectionDaemon(TheStore, TheStore.Tenancy.Default.Database,
            new HighWaterDetector(coordinator, TheStore.Events, Logger), Logger);

        await daemon.UseCoordinator(coordinator);

        Disposables.Add(daemon);
        return daemon;
    }

    public Task WaitForAction(string shardName, ShardAction action, TimeSpan timeout = default)
    {
        if (timeout == default)
        {
            timeout = 30.Seconds();
        }

        return new ShardActionWatcher(_daemon.Tracker, shardName, action, timeout).Task;
    }
}
