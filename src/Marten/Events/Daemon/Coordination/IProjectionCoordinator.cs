using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using Microsoft.Extensions.Hosting;

namespace Marten.Events.Daemon.Coordination;

public interface IProjectionCoordinator : IHostedService
{
    // TODO -- add some convenience methods to get at various shards
    IProjectionDaemon DaemonForMainDatabase();
    ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier);

    ValueTask<IReadOnlyList<IProjectionDaemon>> AllDaemonsAsync();

    /// <summary>
    /// Stops the projection coordinator's automatic restart logic and stops all running agents across all daemons. Does not release any held locks.
    /// </summary>
    /// <returns></returns>
    Task PauseAsync();

    /// <summary>
    /// Resumes the projection coordinators automatic restart logic and starts all running agents across all daemons. Intended to be used after <see cref="PauseAsync"/>
    /// </summary>
    /// <returns></returns>
    Task ResumeAsync();
}

public interface IProjectionCoordinator<T> : IProjectionCoordinator where T : IDocumentStore
{

}
