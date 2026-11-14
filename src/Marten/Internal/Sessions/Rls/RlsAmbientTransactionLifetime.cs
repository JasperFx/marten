#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

internal sealed class RlsAmbientTransactionLifetime: AmbientTransactionLifetime
{
    private readonly string _tenantId;
    private readonly string _settingName;

    public RlsAmbientTransactionLifetime(SessionOptions options, string tenantId, string settingName): base(options)
    {
        _tenantId = tenantId;
        _settingName = settingName;
    }

    protected override Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return RlsSessionVariableApplier.ApplyAsync(connection, _settingName, _tenantId, token);
    }

    protected override Task BeforeCloseAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return RlsSessionVariableApplier.ResetAsync(connection, _settingName, token);
    }
}
