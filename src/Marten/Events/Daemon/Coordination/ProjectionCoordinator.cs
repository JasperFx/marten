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
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Coordination;

public class ProjectionCoordinator<T>: ProjectionCoordinator, IProjectionCoordinator<T> where T : class, IDocumentStore
{
    public ProjectionCoordinator(T documentStore, ILogger<ProjectionCoordinator> logger): base(documentStore, logger)
    {
    }
}

public class ProjectionCoordinator: IProjectionCoordinator
{
    private readonly System.Threading.Lock _daemonLock = new();
    private readonly ILogger<ProjectionCoordinator> _logger;
    private readonly StoreOptions _options;

    private readonly ResiliencePipeline _resilience;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _cancellation;

    private ImHashMap<string, IProjectionDaemon> _daemons = ImHashMap<string, IProjectionDaemon>.Empty;
    private Task? _runner;

    public ProjectionCoordinator(IDocumentStore documentStore, ILogger<ProjectionCoordinator> logger)
    {
        var store = (DocumentStore)documentStore;

        Mode = store.Options.Projections.AsyncMode;

        Distributor = BuildDistributor(store);

        _options = store.Options;
        _logger = logger;
        _resilience = store.Options.ResiliencePipeline;
        _timeProvider = _options.Events.TimeProvider;
        Store = store;
    }

    // 9.0 (#4349 dedupe): the Solo / SingleTenant / MultiTenanted distributors now live in
    // JasperFx.Events. Marten wires them with closures over its own tenancy, shard, and lock
    // surfaces. ProjectionSet (Marten-side) remains the IProjectionSet implementation, and the
    // Postgres lock factory hands back Weasel's AdvisoryLock — which implements
    // JasperFx.Events.Daemon.IAdvisoryLock directly as of Weasel 9.0.0-alpha.7.
    private static IProjectionDistributor BuildDistributor(DocumentStore store)
    {
        var projections = store.Options.Projections;
        var baseLockId = projections.DaemonLockId;

        Func<IEnumerable<ShardName>> allShards = () => projections.AllShards().Select(x => x.Name);

        Func<IProjectionDatabase, IReadOnlyList<ShardName>, int, IProjectionSet> setFactory =
            (db, names, lockId) => new ProjectionSet(lockId, (MartenDatabase)db, names);

        Func<ValueTask<IReadOnlyList<IProjectionDatabase>>> allDatabases = async () =>
        {
            var databases = await store.Storage.AllDatabases().ConfigureAwait(false);
            return databases.OfType<IProjectionDatabase>().ToList();
        };

        switch (projections.AsyncMode)
        {
            case DaemonMode.Solo:
                return new SoloProjectionDistributor(allDatabases, allShards, setFactory, baseLockId);

            case DaemonMode.HotCold:
                var lockFactory = buildLockFactory(store);
                if (store.Options.Tenancy is DefaultTenancy)
                {
                    return new SingleTenantProjectionDistributor(
                        () => (IProjectionDatabase)store.Storage.Database,
                        allShards, lockFactory, setFactory,
                        store.Options.EventGraph.DatabaseSchemaName, baseLockId);
                }

                return new MultiTenantedProjectionDistributor(allDatabases, allShards, lockFactory, setFactory,
                    baseLockId);

            default:
                return null;
        }
    }

    private static Func<IProjectionDatabase, IAdvisoryLock> buildLockFactory(DocumentStore store)
    {
        ILogger logger = store.Options.LogFactory?.CreateLogger<AdvisoryLock>() ??
                         store.Options.DotNetLogger ?? NullLogger<AdvisoryLock>.Instance;

        return db => new AdvisoryLock(((MartenDatabase)db).DataSource, logger, ((MartenDatabase)db).Id.Identity,
            new AdvisoryLockOptions
            {
                LockMonitoringEnabled = store.Options.Events.UseMonitoredAdvisoryLock,
                TransactionalLockEnabled = store.Options.Events.UseAdvisoryLockTransaction
            });
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

                foreach (var set in sets)
                {
                    // Is it already running here?
                    if (Distributor.HasLock(set))
                    {
                        var daemon = resolveDaemon(set);

                        // check if it's still running
                        await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        if (await Distributor.TryAttainLockAsync(set, stoppingToken).ConfigureAwait(false))
                        {
                            var daemon = resolveDaemon(set);

                            // check if it's still running
                            await startAgentsIfNecessaryAsync(set, daemon, stoppingToken).ConfigureAwait(false);
                        }
                        else
                        {
                            // We don't hold the lock, so we might've lost it due to a postgres outage. We should make sure any agents are no longer running on this node.
                            var daemon = resolveDaemon(set);

                            await stopAgentsIfNecessaryAsync(set, daemon).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error trying to attain a lock for set {Name} and lock id {LockId}. Will retry later", set.Names.Select(x => x.Identity).Join(", "), set.LockId);
                        await Task.Delay(_options.Projections.LeadershipPollingTime.Milliseconds(), stoppingToken)
                            .ConfigureAwait(false);
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
