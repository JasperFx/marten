using System.Threading.Tasks;
using Marten.Events.Daemon.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

public class DaemonUsage
{
    #region sample_using_projection_coordinator

    public static async Task accessing_the_daemon(IHost host)
    {
        // IProjectionCoordinator is registered as a singleton in your
        // application by IServiceCollection.AddMarten()

        var coordinator = host.Services.GetRequiredService<IProjectionCoordinator>();

        // If targeting only a single database with Marten
        var daemon = coordinator.DaemonForMainDatabase();
        await daemon.StopAgentAsync("Trip:All");

        // If targeting multiple databases for multi-tenancy
        var daemon2 = await coordinator.DaemonForDatabase("tenant1");
        await daemon2.StopAllAsync();
    }

    #endregion
}
