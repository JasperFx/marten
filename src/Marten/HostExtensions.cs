using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon.Coordination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marten;

public static class HostExtensions
{
    /// <summary>
    /// Testing helper to pause all projection daemons in the system and completely
    /// disable the daemon projection assignments
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static Task PauseAllDaemonsAsync(this IHost host)
    {
        var coordinator =  host.Services.GetRequiredService<IProjectionCoordinator>();
        return coordinator.PauseAsync();
    }

    /// <summary>
    /// Testing helper to resume all projection daemons in the system and restart
    /// the daemon projection assignments
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static Task ResumeAllDaemonsAsync(this IHost host)
    {
        var coordinator =  host.Services.GetRequiredService<IProjectionCoordinator>();
        return coordinator.ResumeAsync();
    }

    /// <summary>
    /// Retrieve the Marten document store for this IHost
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static IDocumentStore DocumentStore(this IHost host)
    {
        return host.Services.GetRequiredService<IDocumentStore>();
    }


}
