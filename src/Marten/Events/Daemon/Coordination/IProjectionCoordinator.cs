using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Marten.Events.Daemon.Coordination;

// TODO -- move this up in the namespace?
public interface IProjectionCoordinator : IHostedService
{
    // TODO -- add some convenience methods to get at various shards
    IProjectionDaemon DaemonForMainDatabase();
    ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier);
}

public interface IProjectionCoordinator<T> : IProjectionCoordinator where T : IDocumentStore
{

}
