using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
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
    /// Testing helper to pause all projection daemons in the system and completely
    /// disable the daemon projection assignments for an ancillary store
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static Task PauseAllDaemonsAsync<T>(this IHost host) where T : IDocumentStore
    {
        var coordinator =  host.Services.GetRequiredService<IProjectionCoordinator<T>>();
        return coordinator.PauseAsync();
    }

    /// <summary>
    /// Testing helper to pause all projection daemons in the system and completely
    /// disable the daemon projection assignments
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static Task PauseAllDaemonsAsync(this IServiceProvider services)
    {
        var coordinator =  services.GetRequiredService<IProjectionCoordinator>();
        return coordinator.PauseAsync();
    }

    /// <summary>
    /// Testing helper to pause all projection daemons in the system and completely
    /// disable the daemon projection assignments for an ancillary store
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static Task PauseAllDaemonsAsync<T>(this IServiceProvider services) where T : IDocumentStore
    {
        var coordinator =  services.GetRequiredService<IProjectionCoordinator<T>>();
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
    /// Retrieve the Marten document store for this IHost
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static IDocumentStore DocumentStore(this IServiceProvider services)
    {
        return services.GetRequiredService<IDocumentStore>();
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
    /// Retrieve the Marten document store for this IHost when working with multiple Marten databases
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T DocumentStore<T>(this IServiceProvider services) where T : IDocumentStore
    {
        return services.GetRequiredService<T>();
    }

    /// <summary>
    /// Override the main Marten DocumentStore and any registered "ancillary" stores that are using the
    /// Async Daemon to run in "Solo" mode for faster and probably more reliable automated testing
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IServiceCollection MartenDaemonModeIsSolo(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureMarten, OverrideDaemonModeToSolo>();
        return services;
    }

    internal class OverrideDaemonModeToSolo: IGlobalConfigureMarten
    {
        public void Configure(IServiceProvider services, StoreOptions options)
        {
            if (options.Projections.AsyncMode == DaemonMode.HotCold)
            {
                options.Projections.AsyncMode = DaemonMode.Solo;
            }
        }
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
    /// Call DocumentStore.ResetAllData() on the document store in this host. This also pauses, then
    /// resumes all asynchronous projection and subscription processing
    /// </summary>
    /// <param name="host"></param>
    public static async Task ResetAllMartenDataAsync(this IHost host)
    {
        var coordinator = host.Services.GetService<IProjectionCoordinator>();
        if (coordinator != null)
        {
            await coordinator.PauseAsync().ConfigureAwait(false);
        }

        var store = host.DocumentStore();
        await store.Advanced.ResetAllData(CancellationToken.None).ConfigureAwait(false);

        if (coordinator != null)
        {
            await coordinator.ResumeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Call DocumentStore.ResetAllData() on the document store in this host when working with multiple Marten databases
    /// </summary>
    /// <param name="host"></param>
    /// <typeparam name="T"></typeparam>
    public static async Task ResetAllMartenDataAsync<T>(this IHost host) where T : IDocumentStore
    {
        var coordinator = host.Services.GetService<IProjectionCoordinator<T>>();
        if (coordinator != null)
        {
            await coordinator.PauseAsync().ConfigureAwait(false);
        }

        var store = host.DocumentStore<T>();
        await store.Advanced.ResetAllData(CancellationToken.None).ConfigureAwait(false);

        if (coordinator != null)
        {
            await coordinator.ResumeAsync().ConfigureAwait(false);
        }
    }

        /// <summary>
    /// Clean off all Marten data in the default DocumentStore for this host
    /// </summary>
    /// <param name="host"></param>
    public static async Task CleanAllMartenDataAsync(this IServiceProvider services)
    {
        var store = services.DocumentStore();
        await store.Advanced.Clean.DeleteAllDocumentsAsync(CancellationToken.None).ConfigureAwait(false);
        await store.Advanced.Clean.DeleteAllEventDataAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Clean off all Marten data in the specified DocumentStore for this host when working with multiple Marten databases
    /// </summary>
    /// <param name="host"></param>
    /// <typeparam name="T"></typeparam>
    public static async Task CleanAllMartenDataAsync<T>(this IServiceProvider services) where T : IDocumentStore
    {
        var store = services.DocumentStore<T>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync(CancellationToken.None).ConfigureAwait(false);
        await store.Advanced.Clean.DeleteAllEventDataAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Call DocumentStore.ResetAllData() on the document store in this host
    /// </summary>
    /// <param name="host"></param>
    public static async Task ResetAllMartenDataAsync(this IServiceProvider services)
    {
        var coordinator = services.GetService<IProjectionCoordinator>();
        if (coordinator != null)
        {
            await coordinator.PauseAsync().ConfigureAwait(false);
        }

        var store = services.DocumentStore();
        await store.Advanced.ResetAllData(CancellationToken.None).ConfigureAwait(false);

        if (coordinator != null)
        {
            await coordinator.ResumeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Call DocumentStore.ResetAllData() on the document store in this host when working with multiple Marten databases
    /// </summary>
    /// <param name="host"></param>
    /// <typeparam name="T"></typeparam>
    public static async Task ResetAllMartenDataAsync<T>(this IServiceProvider services) where T : IDocumentStore
    {
        var coordinator = services.GetService<IProjectionCoordinator<T>>();
        if (coordinator != null)
        {
            await coordinator.PauseAsync().ConfigureAwait(false);
        }

        var store = services.DocumentStore<T>();
        await store.Advanced.ResetAllData(CancellationToken.None).ConfigureAwait(false);

        if (coordinator != null)
        {
            await coordinator.ResumeAsync().ConfigureAwait(false);
        }
    }
}
