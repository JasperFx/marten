using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Daemon.Resiliency;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    internal class AsyncProjectionHostedService<T> : AsyncProjectionHostedService where T : IDocumentStore
    {
        public AsyncProjectionHostedService(T store, ILogger<AsyncProjectionHostedService> logger) : base(store, logger)
        {
        }
    }

    /// <summary>
    /// Registered automatically by Marten if the async projection daemon is enabled
    /// to start and stop asynchronous projections on application start and shutdown
    /// </summary>
    public class AsyncProjectionHostedService : IHostedService
    {
        private readonly ILogger<AsyncProjectionHostedService> _logger;

        public AsyncProjectionHostedService(IDocumentStore store, ILogger<AsyncProjectionHostedService> logger)
        {
            Store = store.As<DocumentStore>();
            _logger = logger;
        }

        internal DocumentStore Store { get; }

        internal IProjectionDaemon Agent { get; private set; }

        internal INodeCoordinator Coordinator { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            switch (Store.Options.Projections.AsyncMode)
            {
                case DaemonMode.Disabled:
                    return;
                case DaemonMode.Solo:
                    Coordinator = new SoloCoordinator();
                    break;
                case DaemonMode.HotCold:
                    Coordinator = new HotColdCoordinator(Store.Tenancy.Default, (DaemonSettings) Store.Options.Projections, _logger);
                    break;
            }

            try
            {
                Agent = await Store.BuildProjectionDaemonAsync(logger:_logger).ConfigureAwait(false);
                await Coordinator.Start(Agent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to start the asynchronous projection agent");
                throw;
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
                await Coordinator.Stop().ConfigureAwait(false);
                await Agent.StopAll().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when trying to stop the asynchronous projection agent");
            }
        }
    }
}
