#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

/// <remarks>
///     <para>
///         Marten 10 (#5729, #5861): the applier is asynchronous, with ONE deliberate exception,
///         <see cref="ResetBeforeSynchronousRelease" />. The synchronous <c>Apply</c> and the general synchronous
///         <c>Reset</c> went with the public synchronous entry points that were their last callers
///         (<c>IQuerySession.Connection</c>, <c>IDocumentSession.BeginTransaction()</c> and the synchronous
///         <c>Serializable</c> session factories).
///     </para>
///     <para>
///         Sessions are still <see cref="IDisposable" />, so a synchronous <c>Dispose()</c> can still release a
///         connection the session opened and set the tenant on. <see cref="ResetBeforeSynchronousRelease" /> is
///         what keeps that tenant from outliving the session.
///     </para>
/// </remarks>
internal static class RlsSessionVariableApplier
{
    private const string SetConfigSql = "SELECT set_config(@name, @value, false)";
    private const string ResetConfigSql = "SELECT set_config(@name, NULL, false)";

    public static async Task ApplyAsync(NpgsqlConnection connection, string settingName, string tenantId, CancellationToken token)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SetConfigSql;
        cmd.Parameters.AddWithValue("name", settingName);
        cmd.Parameters.AddWithValue("value", tenantId);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task ResetAsync(NpgsqlConnection connection, string settingName, CancellationToken token = default)
    {
        if (connection.State != ConnectionState.Open) return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = ResetConfigSql;
        cmd.Parameters.AddWithValue("name", settingName);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resets the tenant setting, synchronously, on a connection the session opened itself, right before a
    ///     synchronous <c>Dispose()</c> hands that connection back to the pool. Never throws.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This is the one synchronous database call left in Marten 10</b>, which otherwise has no
    ///         synchronous data access (#5861). The maintainer kept it on purpose when reviewing #5907. Sessions are
    ///         still <see cref="IDisposable" />, and Npgsql's own reset-on-close discards session state, but a
    ///         connection string with <c>No Reset On Close=true</c> turns that off. Without this call, the session's
    ///         tenant would then survive on the pooled connection into whatever borrows it next. That borrower is
    ///         only exposed if it does not set its own tenant, but it is still a cross-tenant leak, so the
    ///         synchronous <c>Dispose()</c> pays for one round trip to close it.
    ///     </para>
    ///     <para>
    ///         It is deliberately narrow. Only the RLS lifetimes call it, only from synchronous <c>Dispose()</c>, and
    ///         only for a connection the session opened. A connection the caller supplied
    ///         (<c>SessionOptions.ForTransaction</c>, <c>ForConnection</c>) is the caller's, and it is left alone, as
    ///         it always was. <c>DisposeAsync()</c> resets through <see cref="ResetBeforeReleaseAsync" /> instead (#5881).
    ///     </para>
    ///     <para>
    ///         If the reset fails, the failure is logged and <c>Dispose()</c> carries on. The connection must still
    ///         not go back to the pool carrying the tenant. A broken connection never does, because Npgsql discards
    ///         it. A healthy one cannot be discarded on its own through Npgsql's public API, and
    ///         <c>NpgsqlConnection.ClearPool</c> does not reach data-source pools, so the database's data source is
    ///         cleared instead. That closes this connection when it is returned, along with the pool's idle ones. It
    ///         is a heavy remedy, but only for a rare failure.
    ///     </para>
    /// </remarks>
    public static void ResetBeforeSynchronousRelease(NpgsqlConnection connection, string settingName,
        IMartenDatabase? database, IMartenSessionLogger logger)
    {
        if (connection.State != ConnectionState.Open) return;

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = ResetConfigSql;
            cmd.Parameters.AddWithValue("name", settingName);
            cmd.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            withholdFromThePool(connection, settingName, database, logger, e);
        }
    }

    /// <summary>
    ///     #5881. The asynchronous counterpart of <see cref="ResetBeforeSynchronousRelease" />, for the
    ///     <c>DisposeAsync()</c>, commit and rollback paths that hand a connection the session opened back to the
    ///     pool. Same contract: never throws, and a failed reset on a healthy connection clears the pool, so the
    ///     tenant cannot outlive the session on a pooled connection.
    /// </summary>
    /// <remarks>
    ///     Before #5881 these paths called <see cref="ResetAsync" /> directly, so a failed reset threw out of
    ///     <c>DisposeAsync()</c> before the connection was disposed, and the connection was never given back at all.
    ///     The reset deliberately takes no token: a cancelled one would skip it.
    /// </remarks>
    public static async Task ResetBeforeReleaseAsync(NpgsqlConnection connection, string settingName,
        IMartenDatabase? database, IMartenSessionLogger logger)
    {
        if (connection.State != ConnectionState.Open) return;

        try
        {
            await ResetAsync(connection, settingName, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            withholdFromThePool(connection, settingName, database, logger, e);
        }
    }

    /// <summary>
    ///     What both release paths do when the reset failed: log it, and make sure a healthy connection still carrying
    ///     the tenant is closed rather than reused. A broken connection never goes back to the pool, because Npgsql
    ///     discards it. A healthy one cannot be discarded on its own through Npgsql's public API, and
    ///     <c>NpgsqlConnection.ClearPool</c> does not reach data-source pools, so the database's data source is
    ///     cleared instead. That closes this connection when it is returned, along with the pool's idle ones.
    /// </summary>
    private static void withholdFromThePool(NpgsqlConnection connection, string settingName,
        IMartenDatabase? database, IMartenSessionLogger logger, Exception e)
    {
        try
        {
            logger.LogFailure(e,
                $"Failed to reset the row level security setting '{settingName}' before releasing the connection; it will not be returned to the pool");
        }
        catch
        {
            // Logging must not make disposal throw either
        }

        if (connection.FullState.HasFlag(ConnectionState.Broken)) return;

        try
        {
            if (database is MartenDatabase martenDatabase)
            {
                martenDatabase.DataSource.Clear();
            }
            else
            {
                NpgsqlConnection.ClearPool(connection);
            }
        }
        catch
        {
            // Best effort, and disposal must not throw
        }
    }
}
