using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;

namespace Marten.Events.Daemon.Coordination;

/*
 * TODO
 * Use deterministic hash for lock id of the per projection shard lock id
 * Error handling in advisory lock?
 * In ProjectionCoordinator, turn off agents where you no longer have the lock
 * Push through being able to pause a SubscriptionAgent
 * Throw specific exception for ShardStartException
 * Automatically pause agent that gets the projection out of order
 * Pause if grouping fails too many times
 * Pause if applying a batch fails too many times
 * Flesh out IProjectionCoordinator
 * Register ProjectionCoordinator as IHostedService & IProjectionCoordinator
 * Register correct IProjectionDistributor
 * Retrofit old tests for hot/cold detection
 * Start new issue for pausing, stopping, restarting projections/databases
 * Move on to projection command, simplify output
 */

public class ProjectionCoordinator : BackgroundService
{
    private readonly IProjectionDistributor _distributor;
    private readonly StoreOptions _options;
    private readonly ILogger<ProjectionCoordinator> _logger;

    private readonly Dictionary<IMartenDatabase, IProjectionDaemon> _daemons = new();
    private readonly ResiliencePipeline _resilience;

    public ProjectionCoordinator(IProjectionDistributor distributor, StoreOptions options, ILogger<ProjectionCoordinator> logger)
    {
        _distributor = distributor;
        _options = options;
        _logger = logger;
        _resilience = options.ResiliencePipeline;
    }

    internal record DaemonShardName(IProjectionDaemon Daemon, ShardName Name);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _distributor.RandomWait(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sets = await _distributor
                    .BuildDistributionAsync().ConfigureAwait(false);

                foreach (var set in sets)
                {
                    // Is it already running here?
                    if (_distributor.HasLock(set))
                    {
                        var daemon = resolveDaemon(set);

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                    }
                    else if (await _distributor.TryAttainLockAsync(set, stoppingToken).ConfigureAwait(false))
                    {
                        var daemon = resolveDaemon(set);

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                // Only really expect any errors if there are dynamic tenants in place
                _logger.LogError(e, "Error trying to resolve projection distributions");
            }

            if (_daemons.Values.Any(x => x.HasAnyPaused()))
            {
                await Task.Delay(_options.Projections.AgentPauseTime, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(_options.Projections.LeadershipPollingTime.Milliseconds(), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task startAgentsIfNecessaryAsync(IProjectionSet set,
        IProjectionDaemon daemon, CancellationToken stoppingToken)
    {
        foreach (var name in set.Names)
        {
            var agent = daemon.CurrentShards().FirstOrDefault(x => x.Name.Equals(name));
            if (agent == null)
            {
                await tryStartAgent(stoppingToken, daemon, name, set).ConfigureAwait(false);
            }
            else if (agent.Status == AgentStatus.Paused && agent.PausedTime.HasValue && DateTimeOffset.UtcNow.Subtract(agent.PausedTime.Value) > _options.Projections.HealthCheckPollingTime)
            {
                await tryStartAgent(stoppingToken, daemon, name, set).ConfigureAwait(false);
            }
        }
    }

    private IProjectionDaemon resolveDaemon(IProjectionSet set)
    {
        if (!_daemons.TryGetValue(set.Database, out var daemon))
        {
            daemon = set.BuildDaemon();
            _daemons[set.Database] = daemon;
        }

        return daemon;
    }

    private async Task tryStartAgent(CancellationToken stoppingToken, IProjectionDaemon daemon, ShardName name,
        IProjectionSet set)
    {
        try
        {
            await _resilience.ExecuteAsync<DaemonShardName>(
                static (x, t) => new ValueTask(x.Daemon.StartShard(x.Name.Identity, t)),
                new DaemonShardName(daemon, name), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to start subscription {Name} on database {Database}", name.Identity, set.Database.Identifier);
        }
    }
}
