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
    private readonly SessionOptions _sessionOptions;

    public RlsAmbientTransactionLifetime(SessionOptions options, string tenantId, string settingName): base(options)
    {
        _tenantId = tenantId;
        _settingName = settingName;
        _sessionOptions = options;
    }

    protected override void AfterOpened(NpgsqlConnection connection)
    {
        RlsSessionVariableApplier.Apply(connection, _settingName, _tenantId);
    }

    protected override Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return RlsSessionVariableApplier.ApplyAsync(connection, _settingName, _tenantId, token);
    }

    protected override void BeforeClose(NpgsqlConnection connection)
    {
        // #5881: never throws, so a failed reset cannot keep the connection from being disposed
        RlsSessionVariableApplier.ResetBeforeRelease(connection, _settingName, _sessionOptions.Tenant?.Database, Logger);
    }

    protected override Task BeforeCloseAsync(NpgsqlConnection connection, CancellationToken token)
    {
        // #5881: never throws, so a failed reset cannot keep the connection from being disposed
        return RlsSessionVariableApplier.ResetBeforeReleaseAsync(connection, _settingName, _sessionOptions.Tenant?.Database, Logger);
    }
}
