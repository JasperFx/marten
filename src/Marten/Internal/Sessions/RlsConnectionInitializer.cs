#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

/// <summary>
/// Connection initializer that calls <c>set_config</c> on every opened connection
/// so Row Level Security policies can read the current tenant via
/// <c>current_setting(...)</c>. Both the setting name and tenant id are sent as
/// bound parameters, so no value-level escaping is required.
/// </summary>
internal sealed class RlsConnectionInitializer: IConnectionInitializer
{
    private const string SetConfigSql = "SELECT set_config(@name, @value, false)";

    private readonly string _tenantId;
    private readonly string _settingName;

    public RlsConnectionInitializer(string tenantId, string settingName = "app.tenant_id")
    {
        _tenantId = tenantId;
        _settingName = settingName;
    }

    /// <inheritdoc />
    public void Initialize(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = SetConfigSql;
        cmd.Parameters.AddWithValue("name", _settingName);
        cmd.Parameters.AddWithValue("value", _tenantId);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SetConfigSql;
        cmd.Parameters.AddWithValue("name", _settingName);
        cmd.Parameters.AddWithValue("value", _tenantId);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
