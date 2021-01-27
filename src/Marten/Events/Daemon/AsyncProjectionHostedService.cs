using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    public class AsyncProjectionHostedService : IHostedService
    {
        private readonly IProjectionDaemon _agent;
        private readonly INodeCoordinator _coordinator;
        private readonly ILogger<AsyncProjectionHostedService> _logger;

        public AsyncProjectionHostedService(IProjectionDaemon agent, INodeCoordinator coordinator, ILogger<AsyncProjectionHostedService> logger)
        {
            _agent = agent;
            _coordinator = coordinator;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _coordinator.Start(_agent, cancellationToken);
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
                await _coordinator.Stop();
                await _agent.StopAll();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error when trying to stop the asynchronous projection agent");
            }
        }
    }
}
