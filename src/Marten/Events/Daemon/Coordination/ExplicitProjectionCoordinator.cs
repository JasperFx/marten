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

public class ExplicitProjectionCoordinator<T>: ExplicitProjectionCoordinator, IProjectionCoordinator<T> where T : class, IDocumentStore
{
    public ExplicitProjectionCoordinator(T documentStore, ILogger<ExplicitProjectionCoordinator> logger) : base(documentStore, logger)
    {
    }
}

public class ExplicitProjectionCoordinator: IProjectionCoordinator
{
    private readonly System.Threading.Lock _daemonLock = new();
    private readonly ILogger _logger;

    // #5842 (jasperfx#1148): one lifecycle transition at a time. Overlapping callers are real: JasperFx's
    // RunCommand stops the host again when ApplicationStopping fires, so a WebApplicationFactory/Alba teardown
    // has two IHost.StopAsync calls in flight, and both reach this coordinator. Unserialized, both took the same
    // _daemons snapshot, ran StopAllAsync on the same daemons at once and disposed each one twice, possibly
    // while the other caller was still inside its StopAllAsync. A start racing a stop could likewise
    // StartAllAsync a daemon the other caller was disposing.
    private readonly SemaphoreSlim _lifecycle = new(1, 1);


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
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await startAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task startAllAsync()
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

    /// <remarks>
    ///     Safe to call concurrently with itself and with <see cref="StartAsync" />: lifecycle calls are serialized,
    ///     so an overlapping stop waits for the one in flight and then finds no daemons left to stop (#5842). The
    ///     token is deliberately not used for that wait, so a shutdown always completes its teardown.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await stopAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task stopAllAsync()
    {
        // marten#5056 (jasperfx#574): same posture as ProjectionCoordinatorBase.StopAsync. These daemons are about
        // to be disposed, so drop them from the cache -- a repeated Pause/Stop has nothing to fan out over, and the
        // external manager re-acquires fresh daemons through the accessors. The cache is taken and cleared in one
        // step BEFORE the fan-out (#5842): clearing it afterwards also dropped any daemon an accessor created during
        // the stop, which then leaked without ever being stopped or disposed.
        ImHashMap<string, IProjectionDaemon> daemons;
        lock (_daemonLock)
        {
            daemons = _daemons;
            _daemons = ImHashMap<string, IProjectionDaemon>.Empty;
        }

        foreach (var pair in daemons.Enumerate())
        {
            try
            {
                await pair.Value.StopAllAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error while trying to stop daemon agents in database {Name}", pair.Key);
            }

            pair.Value.SafeDispose();
        }
    }

    /// <summary>
    ///     Test seam: put a daemon in the cache as though an accessor had resolved it, so lifecycle tests can run
    ///     against a fake daemon without a database.
    /// </summary>
    internal void UseDaemon(string databaseIdentifier, IProjectionDaemon daemon)
    {
        lock (_daemonLock)
        {
            _daemons = _daemons.AddOrUpdate(databaseIdentifier, daemon);
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
