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

public class ExplicitProjectionCoordinator<T>: ExplicitProjectionCoordinator, IProjectionCoordinator<T> where T : IDocumentStore
{
    public ExplicitProjectionCoordinator(T documentStore, ILogger<ExplicitProjectionCoordinator> logger) : base(documentStore, logger)
    {
    }
}

public class ExplicitProjectionCoordinator: IProjectionCoordinator
{
    private readonly object _daemonLock = new();
    private readonly ILogger _logger;


    private ImHashMap<string, IProjectionDaemon> _daemons = ImHashMap<string, IProjectionDaemon>.Empty;

    public ExplicitProjectionCoordinator(IDocumentStore documentStore, ILogger<ExplicitProjectionCoordinator> logger)
    {
        var store = (DocumentStore)documentStore;

        Mode = store.Options.Projections.AsyncMode;

        _logger = logger;
        Store = store;
    }

    public DaemonMode Mode { get; }

    public DocumentStore Store { get; }

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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in _daemons.Enumerate())
        {
            try
            {
                await pair.Value.StartAllAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error while trying to stop daemon agents in database {Name}", pair.Key);
            }
        }
    }

    public Task PauseAsync()
    {
        return StopAsync(CancellationToken.None);
    }

    public Task ResumeAsync()
    {
        return StartAsync(default);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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
