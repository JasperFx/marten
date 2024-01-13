#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon;

/// <summary>
///     Swappable coordinator for the async daemon that is
///     responsible for starting projection shards and assigning
///     work to the locally running IProjectionDaemon
/// </summary>
public interface INodeCoordinator: IDisposable
{
    /// <summary>
    ///     Current daemon being controlled
    /// </summary>
    IProjectionDaemon? Daemon { get; }

    /// <summary>
    /// Indicates if the current coordinator is responsible for running the async daemon.
    /// Will always be true in single-node environments.
    /// </summary>
    bool IsPrimary { get; }

    /// <summary>
    ///     Called at the start of the application to register the projection
    ///     daemon with the coordinator
    /// </summary>
    /// <param name="daemon"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task Start(IProjectionDaemon daemon, CancellationToken token);

    /// <summary>
    ///     Called at application shutdown as a hook to perform
    ///     any work necessary to take the current node down
    /// </summary>
    /// <returns></returns>
    Task Stop();
}
