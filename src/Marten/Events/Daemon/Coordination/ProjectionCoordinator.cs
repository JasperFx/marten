using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Polly;

namespace Marten.Events.Daemon.Coordination;

public class ProjectionCoordinator<T>: ProjectionCoordinator, IProjectionCoordinator<T> where T : IDocumentStore
{
    public ProjectionCoordinator(T documentStore, ILogger<ProjectionCoordinator> logger): base(documentStore, logger)
    {
    }
}

public class ProjectionCoordinator: IProjectionCoordinator
{
    private readonly object _daemonLock = new();
    private readonly ILogger<ProjectionCoordinator> _logger;
    private readonly StoreOptions _options;

    private readonly ResiliencePipeline _resilience;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _cancellation;

    private ImHashMap<string, IProjectionDaemon> _daemons = ImHashMap<string, IProjectionDaemon>.Empty;
    private Task? _runner;

    // Ownership tallies for recordOwnership(). Only ever touched from the single executeAsync loop.
    private readonly OwnershipTracker _ownership;

    public ProjectionCoordinator(IDocumentStore documentStore, ILogger<ProjectionCoordinator> logger)
    {
        var store = (DocumentStore)documentStore;

        Mode = store.Options.Projections.AsyncMode;

        if (store.Options.Projections.AsyncMode == DaemonMode.Solo)
        {
            Distributor = new SoloProjectionDistributor(store);
        }
        else if (store.Options.Projections.AsyncMode == DaemonMode.HotCold)
        {
            if (store.Options.Tenancy is DefaultTenancy)
            {
                Distributor = new SingleTenantProjectionDistributor(store);
            }
            else
            {
                Distributor = new MultiTenantedProjectionDistributor(store);
            }
        }

        _options = store.Options;
        _logger = logger;
        _resilience = store.Options.ResiliencePipeline;
        _timeProvider = _options.Events.TimeProvider;
        _ownership = new OwnershipTracker(_timeProvider);
        Store = store;
    }

    public DaemonMode Mode { get; }

    public DocumentStore Store { get; }

    public IProjectionDistributor Distributor { get; }

    public IProjectionDaemon DaemonForMainDatabase()
    {
        var database = (MartenDatabase)Store.Tenancy.Default.Database;

        return findDaemonForDatabase(database);
    }

    public async ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier)
    {
        var database =
            (MartenDatabase)await Store.Storage.FindOrCreateDatabase(databaseIdentifier).ConfigureAwait(false);
        return findDaemonForDatabase(database);
    }

    public async ValueTask<IReadOnlyList<IProjectionDaemon>> AllDaemonsAsync()
    {
        var all = await Store.Storage.AllDatabases().ConfigureAwait(false);
        return all.OfType<MartenDatabase>().Select(findDaemonForDatabase).ToList();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellation?.SafeDispose();

        _cancellation = new CancellationTokenSource();
        _runner = Task.Run(() => executeAsync(_cancellation.Token), _cancellation.Token);

        return Task.CompletedTask;
    }

    public async Task PauseAsync()
    {
        _logger.LogInformation("Pausing ProjectionCoordinator");
        if (_cancellation != null)
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        await pauseDistributor().ConfigureAwait(false);

        foreach (var pair in _daemons.Enumerate())
        {
            try
            {
                await pair.Value.StopAllAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error while trying to stop daemon agents in database {Name}", pair.Key);
            }
        }
    }

    private async Task pauseDistributor()
    {
        if (_runner == null) return;

        try
        {
#pragma warning disable VSTHRD003
            await _runner.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (TaskCanceledException)
        {
            // Nothing, just from shutting down
        }
        catch (OperationCanceledException)
        {
            // Nothing, just from shutting down
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while trying to stop the ProjectionCoordinator");
        }
    }

    public Task ResumeAsync()
    {
        return StartAsync(default);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await PauseAsync().ConfigureAwait(false);

        foreach (var daemon in _daemons.Enumerate()) daemon.Value.SafeDispose();

        try
        {
            await Distributor.ReleaseAllLocks().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to release subscription agent locks");
        }
    }

    private IProjectionDaemon findDaemonForDatabase(MartenDatabase database)
    {
        if (_daemons.TryFind(database.Identifier, out var daemon))
        {
            return daemon;
        }

        lock (_daemonLock)
        {
            if (_daemons.TryFind(database.Identifier, out daemon))
            {
                return daemon;
            }

            daemon = database.StartProjectionDaemon(Store, _logger);
            _daemons = _daemons.AddOrUpdate(database.Identifier, daemon);
        }

        return daemon;
    }

    private async Task executeAsync(CancellationToken stoppingToken)
    {
        await Distributor.RandomWait(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sets = await Distributor
                    .BuildDistributionAsync().ConfigureAwait(false);

                var owned = 0;
                var denied = 0;

                foreach (var set in sets)
                {
                    // Is it already running here?
                    if (Distributor.HasLock(set))
                    {
                        var daemon = resolveDaemon(set);

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                        owned++;
                        continue;
                    }

                    try
                    {
                        if (await Distributor.TryAttainLockAsync(set, stoppingToken).ConfigureAwait(false))
                        {
                            var daemon = resolveDaemon(set);

                            // check if it's still running
                            await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                            owned++;
                        }
                        else
                        {
                            // We don't hold the lock, so we might've lost it due to a postgres outage. We should make sure any agents are no longer running on this node.
                            var daemon = resolveDaemon(set);

                            await stopAgentsIfNecessaryAsync(set, daemon).ConfigureAwait(false);
                            denied++;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error trying to attain a lock for set {Name} and lock id {LockId}. Will retry later", set.Names.Select(x => x.Identity).Join(", "), set.LockId);
                        denied++;
                        await Task.Delay(_options.Projections.LeadershipPollingTime.Milliseconds(), stoppingToken)
                            .ConfigureAwait(false);
                    }
                }

                recordOwnership(owned, denied, sets.Count);
            }
            catch (Exception e)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                // Only really expect any errors if there are dynamic tenants in place
                _logger.LogError(e, "Error trying to resolve projection distributions");
            }

            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (_daemons.Enumerate().Any(x => x.Value.HasAnyPaused()))
                {
                    await Task.Delay(_options.Projections.AgentPauseTime, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(_options.Projections.LeadershipPollingTime.Milliseconds(), stoppingToken)
                        .ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
                // just get out of here, this signals a graceful shutdown attempt
            }
            catch (OperationCanceledException)
            {
                // Nothing, just from shutting down
            }
        }
    }


    /// <summary>
    ///     Log this node's share of the projection sets, so that "this node is running nothing" is visible
    ///     in telemetry rather than having to be inferred from the absence of other log lines.
    /// </summary>
    /// <remarks>
    ///     The lock-denied branch of the polling loop above was completely silent: no log, no metric, no
    ///     health signal. Only a thrown exception produced any output. That made a node which had been
    ///     denied every database's lock indistinguishable from a healthy node on an idle system, and hid a
    ///     ~35 minute projection outage after an ungraceful node kill left session-scoped advisory locks
    ///     alive server-side until Postgres's TCP layer timed the dead peer out.
    ///
    ///     Steady state is quiet: the Information line is only written when the tally actually changes.
    ///     The warning is the part that cannot be made both loud and quiet at once -- a node can observe
    ///     "I own nothing" but never "nobody owns this", so on a real multi-node HotCold cluster the
    ///     standbys will warn on their repeat cadence. That is why the threshold is configurable and can
    ///     be switched off outright.
    /// </remarks>
    private void recordOwnership(int owned, int denied, int total)
    {
        var report = _ownership.Record(owned, denied, total,
            _options.Projections.NoOwnedProjectionSetsWarningThreshold,
            _options.Projections.NoOwnedProjectionSetsRepeatTime);

        if (report.TallyChanged)
        {
            _logger.LogInformation(
                "ProjectionCoordinator owns {Owned} of {Total} projection sets ({Denied} held by another node)",
                owned, total, denied);
        }

        if (!report.ShouldWarn) return;

        _logger.LogWarning(
            "ProjectionCoordinator has owned none of the {Total} known projection sets for {Elapsed}, so no asynchronous projections or subscriptions are running on this node. Every leadership lock is held elsewhere -- if no other node is running them, the likely cause is a previous node that was killed without releasing its session-scoped advisory locks, which Postgres will hold until it detects the dead client. This is expected on a standby node in a multi-node HotCold cluster; set StoreOptions.Projections.NoOwnedProjectionSetsWarningThreshold to null to silence it",
            total, report.OwnedNothingFor);
    }

    private async Task startAgentsIfNecessaryAsync(IProjectionSet set,
        IProjectionDaemon daemon, CancellationToken stoppingToken)
    {
        foreach (var name in set.Names)
        {
            var agent = daemon.CurrentAgents().FirstOrDefault(x => x.Name.Equals(name));
            if (agent == null)
            {
                await tryStartAgent(stoppingToken, daemon, name, set).ConfigureAwait(false);
            }
            else if (agent is { Status: AgentStatus.Paused, PausedTime: not null } &&
                     _timeProvider.GetUtcNow().Subtract(agent.PausedTime.Value) >
                     _options.Projections.HealthCheckPollingTime)
            {
                await tryStartAgent(stoppingToken, daemon, name, set).ConfigureAwait(false);
            }
            else if (agent.Status == AgentStatus.Stopped)
            {
                // A stranded agent. SubscriptionAgent.ReportCriticalFailureAsync sets its own Status and
                // disposes itself, but never calls back into the daemon's StopAgentAsync -- so the corpse
                // stays in the daemon's agent map. A ProgressionProgressOutOfOrderException lands it here
                // with Status == Stopped and PausedTime == null, which matched neither branch above: the
                // shard was dead, this node still held the set's lock, and nothing ever restarted it or
                // released the lock. Paused agents self-heal through the branch above; Stopped ones did not.
                //
                // Restarting is safe and idempotent. Daemon.StartAgentAsync builds a *fresh* agent for the
                // shard and AddOrUpdates it over the dead entry, and its own guard only declines when the
                // existing agent is still Running.
                _logger.LogWarning(
                    "Restarting stranded projection agent {ShardName} on database {Database}: the agent is in a Stopped state while this node still holds the leadership lock",
                    name.Identity, set.Database.Identifier);

                await tryStartAgent(stoppingToken, daemon, name, set).ConfigureAwait(false);
            }
        }
    }

    private async Task stopAgentsIfNecessaryAsync(IProjectionSet set, IProjectionDaemon daemon)
    {
        foreach (var shardName in set.Names)
        {
            var status = daemon.StatusFor(shardName.Identity);
            if (status == AgentStatus.Running)
            {
                await daemon.StopAgentAsync(shardName.Identity).ConfigureAwait(false);
            }

        }
    }

    private IProjectionDaemon resolveDaemon(IProjectionSet set)
    {
        return findDaemonForDatabase((MartenDatabase)set.Database);
    }

    private async Task tryStartAgent(CancellationToken stoppingToken, IProjectionDaemon daemon, ShardName name,
        IProjectionSet set)
    {
        try
        {
            await _resilience.ExecuteAsync(
                static (x, t) => new ValueTask(x.Daemon.StartAgentAsync(x.Name.Identity, t)),
                new DaemonShardName(daemon, name), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to start subscription {Name} on database {Database}", name.Identity,
                set.Database.Identifier);
            if (daemon.StatusFor(name.Identity) == AgentStatus.Paused)
            {
                daemon.EjectPausedShard(name.Identity);
            }

            await Distributor.ReleaseLockAsync(set).ConfigureAwait(false);
        }
    }

    internal record DaemonShardName(IProjectionDaemon Daemon, ShardName Name);
}
