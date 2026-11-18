#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

internal static class LifetimeCleanup
{
    /// <summary>
    ///     #6074. Rolls back a transaction after the failure the caller is about to rethrow, without letting the
    ///     rollback replace that failure.
    /// </summary>
    /// <remarks>
    ///     A failure that breaks the connection -- a command timeout whose cancellation goes unanswered, a
    ///     terminated backend -- makes Npgsql dispose the transaction with the connector, and the server has
    ///     already rolled it back. Rolling it back again throws <see cref="ObjectDisposedException" /> for
    ///     <c>NpgsqlTransaction</c>, which used to escape in place of the timeout or termination. As #5688 does for
    ///     the sticky lifetime, a failed rollback is logged and swallowed. Never the caller's token either: Npgsql
    ///     answers an already-cancelled token by throwing without sending <c>ROLLBACK</c>.
    /// </remarks>
    public static async Task RollbackAfterFailureAsync(this NpgsqlTransaction transaction,
        IMartenSessionLogger? logger = null)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                logger?.LogFailure(e, "Failure trying to roll back a transaction after a failure");
            }
            catch
            {
                // Logging must not replace the original exception either
            }
        }
    }

    /// <summary>
    ///     #5881. Disposes a connection lifetime that failed before anything else took ownership of it, so its
    ///     connection goes back to the pool. A failure here is logged and swallowed: the caller is already
    ///     rethrowing the exception that matters, and must not have it replaced by this one.
    /// </summary>
    public static async ValueTask DisposeAfterFailureAsync(this IConnectionLifetime lifetime,
        IMartenSessionLogger? logger = null)
    {
        try
        {
            await lifetime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                logger?.LogFailure(e, "Failure trying to dispose a connection lifetime after it failed to start");
            }
            catch
            {
                // Logging must not replace the original exception either
            }
        }
    }
}
