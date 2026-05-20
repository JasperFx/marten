using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImTools;
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

// 9.0 (#4516 dedupe): the leadership-election + agent-lifecycle loop now lives in
// JasperFx.Events.Daemon.ProjectionCoordinatorBase. Marten supplies the distributor +
// settings via the base ctor and implements the daemon-resolution seams, keeping its
// own ImHashMap + double-checked-lock daemon cache.
public class ProjectionCoordinator: ProjectionCoordinatorBase, IProjectionCoordinator
{
    private readonly System.Threading.Lock _daemonLock = new();
    private readonly ILogger _logger;

    private ImHashMap<string, IProjectionDaemon> _daemons = ImHashMap<string, IProjectionDaemon>.Empty;

    public ProjectionCoordinator(IDocumentStore documentStore, ILogger<ProjectionCoordinator> logger)
        : this((DocumentStore)documentStore, logger)
    {
    }

    private ProjectionCoordinator(DocumentStore store, ILogger<ProjectionCoordinator> logger)
        : base(
            BuildDistributor(store),
            logger,
            store.Options.ResiliencePipeline,
            store.Options.Events.TimeProvider,
            store.Options.Projections.LeadershipPollingTime.Milliseconds(),
            store.Options.Projections.AgentPauseTime,
            store.Options.Projections.HealthCheckPollingTime)
    {
        Mode = store.Options.Projections.AsyncMode;
        Store = store;
        _logger = logger;
    }

    public DaemonMode Mode { get; }

    public DocumentStore Store { get; }

    // 9.0 (#4349 dedupe): the Solo / SingleTenant / MultiTenanted distributors live in
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
                // DaemonMode.Disabled: no async daemon, so there is nothing to distribute.
                // Return a no-op distributor rather than null. The #4516 dedupe lifted the
                // coordinator loop into JasperFx.Events.Daemon.ProjectionCoordinatorBase,
                // whose ctor now rejects a null distributor — and a ProjectionCoordinator can
                // legitimately be *constructed* (but never started) in Disabled mode, e.g. when
                // Wolverine's ancillary-store integration resolves IProjectionCoordinator<T>
                // for a store that delegates subscription distribution to Wolverine
                // (AncillaryWolverineOptionsMartenExtensions). Pre-dedupe this was a null
                // assignment that constructed fine; the no-op distributor preserves that while
                // satisfying the lifted ctor's non-null contract.
                return new NulloProjectionDistributor();
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

    protected override IProjectionDaemon ResolveDaemon(IProjectionSet set)
    {
        return findDaemonForDatabase((MartenDatabase)set.Database);
    }

    protected override IReadOnlyList<IProjectionDaemon> ResolvedDaemons()
    {
        return _daemons.Enumerate().Select(x => x.Value).ToList();
    }

    public override IProjectionDaemon DaemonForMainDatabase()
    {
        var database = (MartenDatabase)Store.Tenancy.Default.Database;

        return findDaemonForDatabase(database);
    }

    public override async ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier)
    {
        var database =
            (MartenDatabase)await Store.Storage.FindOrCreateDatabase(databaseIdentifier).ConfigureAwait(false);
        return findDaemonForDatabase(database);
    }

    public override async ValueTask<IReadOnlyList<IProjectionDaemon>> AllDaemonsAsync()
    {
        var all = await Store.Storage.AllDatabases().ConfigureAwait(false);
        return all.OfType<MartenDatabase>().Select(findDaemonForDatabase).ToList();
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
}

// No-op distributor for DaemonMode.Disabled. A ProjectionCoordinator can be *constructed*
// (but never started) when the async daemon is disabled — e.g. Wolverine's ancillary-store
// integration resolves IProjectionCoordinator<T> for stores whose subscription distribution
// it manages itself. The #4516 dedupe lifted the coordinator loop into JasperFx.Events'
// ProjectionCoordinatorBase, whose ctor rejects a null distributor; this preserves the
// pre-dedupe "constructible while disabled" behavior without weakening that contract. Every
// member is a benign no-op (empty distribution, no locks), so it is also safe if ever run.
internal sealed class NulloProjectionDistributor : IProjectionDistributor
{
    public ValueTask<IReadOnlyList<IProjectionSet>> BuildDistributionAsync()
        => new(Array.Empty<IProjectionSet>());

    public Task RandomWait(System.Threading.CancellationToken token) => Task.CompletedTask;

    public bool HasLock(IProjectionSet set) => false;

    public Task<bool> TryAttainLockAsync(IProjectionSet set, System.Threading.CancellationToken token)
        => Task.FromResult(false);

    public Task ReleaseLockAsync(IProjectionSet set) => Task.CompletedTask;

    public Task ReleaseAllLocks() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
