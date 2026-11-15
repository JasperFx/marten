#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

internal static class RlsSessionVariableApplier
{
    private const string SetConfigSql = "SELECT set_config(@name, @value, false)";

    public static void Apply(NpgsqlConnection connection, string settingName, string tenantId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SetConfigSql;
        cmd.Parameters.AddWithValue("name", settingName);
        cmd.Parameters.AddWithValue("value", tenantId);
        cmd.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(NpgsqlConnection connection, string settingName, string tenantId, CancellationToken token)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SetConfigSql;
        cmd.Parameters.AddWithValue("name", settingName);
        cmd.Parameters.AddWithValue("value", tenantId);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static void Reset(NpgsqlConnection connection, string settingName)
    {
        if (connection.State != ConnectionState.Open) return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config(@name, NULL, false)";
        cmd.Parameters.AddWithValue("name", settingName);
        cmd.ExecuteNonQuery();
    }

    public static async Task ResetAsync(NpgsqlConnection connection, string settingName, CancellationToken token = default)
    {
        if (connection.State != ConnectionState.Open) return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config(@name, NULL, false)";
        cmd.Parameters.AddWithValue("name", settingName);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     #5881. Resets the tenant setting before a connection the lifetime is releasing goes back to the pool, and
    ///     never throws. A failed reset is logged, and a healthy connection that still carries the tenant is kept out
    ///     of the pool, so it cannot reach whatever borrows it next.
    /// </summary>
    /// <remarks>
    ///     Before #5881 the release paths called <see cref="Reset" /> directly. A reset that threw skipped the
    ///     dispose that followed it, so <c>Dispose()</c> threw and the connection was never given back.
    /// </remarks>
    public static void ResetBeforeRelease(NpgsqlConnection connection, string settingName, IMartenDatabase? database,
        IMartenSessionLogger logger)
    {
        if (connection.State != ConnectionState.Open) return;

        try
        {
            Reset(connection, settingName);
        }
        catch (Exception e)
        {
            withholdFromThePool(connection, settingName, database, logger, e);
        }
    }

    /// <summary>
    ///     #5881. The asynchronous twin of <see cref="ResetBeforeRelease" />. The reset deliberately takes no token: a
    ///     cancelled one would skip it.
    /// </summary>
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
    ///     cleared instead. That closes this connection when it is returned, along with the pool's idle ones. It is a
    ///     heavy remedy, but only for a rare failure.
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
