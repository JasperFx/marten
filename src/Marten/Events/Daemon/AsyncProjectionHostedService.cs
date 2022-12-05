using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.Resiliency;
using Marten.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

internal class AsyncProjectionHostedService<T>: AsyncProjectionHostedService where T : IDocumentStore
{
    public AsyncProjectionHostedService(T store, ILogger<AsyncProjectionHostedService> logger): base(store, logger)
    {
    }
}

/// <summary>
///     Registered automatically by Marten if the async projection daemon is enabled
///     to start and stop asynchronous projections on application start and shutdown
/// </summary>
public class AsyncProjectionHostedService: IHostedService
{
    private readonly List<INodeCoordinator> _coordinators = new();
    private readonly ILogger<AsyncProjectionHostedService> _logger;

    public AsyncProjectionHostedService(IDocumentStore store, ILogger<AsyncProjectionHostedService> logger)
    {
        Store = store.As<DocumentStore>();
        _logger = logger;
    }

    public IReadOnlyList<INodeCoordinator> Coordinators => _coordinators;

    internal DocumentStore Store { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Store.Options.Projections.AsyncMode == DaemonMode.Disabled)
        {
            return;
        }

        var databases = await Store.Tenancy.BuildDatabases().ConfigureAwait(false);
        foreach (var database in databases.OfType<MartenDatabase>())
        {
            INodeCoordinator coordinator = Store.Options.Projections.AsyncMode == DaemonMode.Solo
                ? new SoloCoordinator()
                : new HotColdCoordinator(database, Store.Options.Projections, _logger);

            try
            {
                var agent = database.StartProjectionDaemon(Store, _logger);
                await coordinator.Start(agent, cancellationToken).ConfigureAwait(false);
                _coordinators.Add(coordinator);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to start the asynchronous projection agent for database {Database}",
                    database.Identifier);
                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Store.Options.Projections.AsyncMode == DaemonMode.Disabled)
        {
            return;
        }

        try
        {
            _logger.LogDebug("Stopping the asynchronous projection agent");
            foreach (var coordinator in _coordinators) await coordinator.Stop().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error when trying to stop the asynchronous projection agent");
        }
    }
}
