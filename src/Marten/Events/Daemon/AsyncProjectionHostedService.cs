using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Resiliency;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Registered automatically by Marten if the async projection daemon is enabled
    /// to start and stop asynchronous projections on application start and shutdown
    /// </summary>
    public class AsyncProjectionHostedService : IHostedService
    {
        private readonly IDocumentStore _store;
        private readonly ILogger<AsyncProjectionHostedService> _logger;

        public AsyncProjectionHostedService(IDocumentStore store, ILogger<AsyncProjectionHostedService> logger)
        {
            _store = store;
            _logger = logger;
        }

        internal IProjectionDaemon Agent { get; private set; }

        internal INodeCoordinator Coordinator { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            switch (_store.Options.Events.Daemon.Mode)
            {
                case DaemonMode.Disabled:
                    return;
                case DaemonMode.Solo:
                    Coordinator = new SoloCoordinator();
                    break;
                case DaemonMode.HotCold:
                    Coordinator = new HotColdCoordinator(_store, (DaemonSettings) _store.Options.Events.Daemon, _logger);
                    break;
            }

            try
            {
                Agent = _store.BuildProjectionDaemon(_logger);
                await Coordinator.Start(Agent, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to start the asynchronous projection agent");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Coordinator.Stop();
                await Agent.StopAll();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when trying to stop the asynchronous projection agent");
            }
        }
    }
}
