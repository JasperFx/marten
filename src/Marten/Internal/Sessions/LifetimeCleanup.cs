#nullable enable
using System;
using System.Threading.Tasks;

namespace Marten.Internal.Sessions;

internal static class LifetimeCleanup
{
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
