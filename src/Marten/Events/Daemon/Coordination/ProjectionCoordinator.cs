using System;
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
    private CancellationTokenSource _cancellation;

    private ImHashMap<string, IProjectionDaemon> _daemons = ImHashMap<string, IProjectionDaemon>.Empty;
    private Task _runner;

    public ProjectionCoordinator(IDocumentStore documentStore, ILogger<ProjectionCoordinator> logger)
    {
        var store = (DocumentStore)documentStore;

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
        Store = store;
    }

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
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif

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

                foreach (var set in sets)
                {
                    // Is it already running here?
                    if (Distributor.HasLock(set))
                    {
                        var daemon = resolveDaemon(set);

                        // If any agents are known to be stopped, we need to just shut
                        // down everything and release the lock, then let some other node pick that up
                        if (anyAgentsAreStoppped(set, daemon))
                        {
                            await stopAndReleaseProjectionSet(set, daemon).ConfigureAwait(false);
                        }

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                    }
                    else if (await Distributor.TryAttainLockAsync(set, stoppingToken).ConfigureAwait(false))
                    {
                        var daemon = resolveDaemon(set);

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                    }
                }
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

    private async Task stopAndReleaseProjectionSet(IProjectionSet set, IProjectionDaemon daemon)
    {
        // No, shut them all down!!!!
        foreach (var shardName in set.Names)
        {
            await daemon.StopAgentAsync(shardName.Identity).ConfigureAwait(false);
        }

        await Distributor.ReleaseLockAsync(set).ConfigureAwait(false);
    }

    private bool anyAgentsAreStoppped(IProjectionSet set, IProjectionDaemon daemon)
    {
        foreach (var name in set.Names)
        {
            var status = daemon.StatusFor(name.Identity);
            if (status == AgentStatus.Stopped)
            {
                return true;
            }
        }

        return false;
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
