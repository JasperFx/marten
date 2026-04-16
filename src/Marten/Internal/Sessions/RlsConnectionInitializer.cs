#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Internal.Sessions;

/// <summary>
/// Connection initializer that issues a PostgreSQL <c>SET</c> statement on every
/// opened connection so Row Level Security policies can read the current tenant
/// via <c>current_setting(...)</c>.
/// </summary>
internal sealed partial class RlsConnectionInitializer: IConnectionInitializer
{
    private readonly string _tenantId;
    private readonly string _settingName;

    /// <summary>
    /// Create a new initializer for the given tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier to set as the session variable value. Must match <c>[a-zA-Z0-9\-_]+</c>.</param>
    /// <param name="settingName">The PostgreSQL session setting name to use. Defaults to <c>app.tenant_id</c>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="tenantId"/> contains characters outside the allowed set.</exception>
    public RlsConnectionInitializer(string tenantId, string settingName = "app.tenant_id")
    {
        if (!TenantIdPattern().IsMatch(tenantId))
        {
            throw new ArgumentException(
                $"Tenant ID '{tenantId}' contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.",
                nameof(tenantId));
        }

        _tenantId = tenantId;
        _settingName = settingName;
    }

    /// <inheritdoc />
    public void Initialize(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET {_settingName} = '{_tenantId}'";
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET {_settingName} = '{_tenantId}'";
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$")]
    private static partial Regex TenantIdPattern();
}
