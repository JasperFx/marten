#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

/// <summary>
/// Hook for executing per-connection initialization logic (e.g. session settings)
/// after a <see cref="NpgsqlConnection"/> has been opened by a session lifetime.
/// </summary>
internal interface IConnectionInitializer
{
    /// <summary>
    /// Synchronously initialize the supplied open connection.
    /// </summary>
    /// <param name="connection">An already-opened connection to initialize.</param>
    void Initialize(NpgsqlConnection connection);

    /// <summary>
    /// Asynchronously initialize the supplied open connection.
    /// </summary>
    /// <param name="connection">An already-opened connection to initialize.</param>
    /// <param name="token">Cancellation token.</param>
    Task InitializeAsync(NpgsqlConnection connection, CancellationToken token);
}
