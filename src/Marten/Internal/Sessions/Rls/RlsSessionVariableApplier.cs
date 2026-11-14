#nullable enable
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

/// <remarks>
///     Marten 10 (#5729): every command path is async, so <see cref="ApplyAsync" /> and
///     <see cref="ResetAsync" /> are the applier. The synchronous <see cref="Apply" /> and
///     <see cref="Reset" /> remain only for the public synchronous entry points that still open or close
///     a connection without awaiting: <c>IQuerySession.Connection</c>, <c>IDocumentSession.BeginTransaction()</c>,
///     the synchronous <c>Serializable</c> session factories, and synchronous <c>Dispose()</c>. They go when
///     those APIs do.
/// </remarks>
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
}
