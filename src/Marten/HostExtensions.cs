using System;
using System.Threading;
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

    /// <summary>
    /// Retrieve the Marten document store for this IHost when working with multiple Marten databases
    /// </summary>
    /// <param name="host"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T DocumentStore<T>(this IHost host) where T : IDocumentStore
    {
        return host.Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Clean off all Marten data in the default DocumentStore for this host
    /// </summary>
    /// <param name="host"></param>
    public static async Task CleanAllMartenDataAsync(this IHost host)
    {
        var store = host.DocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync(CancellationToken.None).ConfigureAwait(false);
        await store.Advanced.Clean.DeleteAllEventDataAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Clean off all Marten data in the specified DocumentStore for this host when working with multiple Marten databases
    /// </summary>
    /// <param name="host"></param>
    /// <typeparam name="T"></typeparam>
    public static async Task CleanAllMartenDataAsync<T>(this IHost host) where T : IDocumentStore
    {
        var store = host.DocumentStore<T>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync(CancellationToken.None).ConfigureAwait(false);
        await store.Advanced.Clean.DeleteAllEventDataAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Call DocumentStore.ResetAllData() on the document store in this host
    /// </summary>
    /// <param name="host"></param>
    public static Task ResetAllMartenDataAsync(this IHost host)
    {
        var store = host.DocumentStore();
        return store.Advanced.ResetAllData(CancellationToken.None);
    }
}
