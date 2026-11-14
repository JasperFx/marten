#nullable enable
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

/// <remarks>
///     Marten 10 (#5729, #5861): the applier is asynchronous only. The synchronous <c>Apply</c>/<c>Reset</c>
///     went with the public synchronous entry points that were their last callers --
///     <c>IQuerySession.Connection</c>, <c>IDocumentSession.BeginTransaction()</c> and the synchronous
///     <c>Serializable</c> session factories. A synchronous <c>Dispose()</c> no longer resets the setting:
///     Npgsql discards session state when the connection goes back to its pool (unless the connection string
///     sets <c>No Reset On Close</c>), and every Marten RLS lifetime applies its own tenant on open anyway.
///     <c>DisposeAsync()</c> still resets it, which matters for a connection the caller supplied
///     (<c>SessionOptions.ForTransaction</c>): that one is not returned to the pool by the session, so after a
///     synchronous <c>Dispose()</c> it keeps the session's tenant setting.
/// </remarks>
internal static class RlsSessionVariableApplier
{
    private const string SetConfigSql = "SELECT set_config(@name, @value, false)";

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
        cmd.CommandText = "SELECT set_config(@name, NULL, false)";
        cmd.Parameters.AddWithValue("name", settingName);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
