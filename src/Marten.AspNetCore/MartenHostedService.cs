using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;
using Microsoft.Extensions.Hosting;

namespace Marten.AspNetCore
{
    public sealed class MartenHostedService : IHostedService
    {
        private readonly IDocumentStore documentStore;
        private IDaemon daemon;

        public MartenHostedService(IDocumentStore documentStore)
        {
            this.documentStore = documentStore;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            daemon = documentStore.BuildProjectionDaemon();
            daemon.StartAll();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return daemon.StopAll();
        }
    }
}
