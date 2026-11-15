#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

internal class RlsTransactionalConnection: TransactionalConnection
{
    private readonly string _tenantId;
    private readonly string _settingName;

    public RlsTransactionalConnection(SessionOptions options, string tenantId, string settingName): base(options)
    {
        _tenantId = tenantId;
        _settingName = settingName;
    }

    protected override Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return RlsSessionVariableApplier.ApplyAsync(connection, _settingName, _tenantId, token);
    }

    // #5861: the one synchronous call Marten 10 keeps. See RlsSessionVariableApplier.ResetBeforeSynchronousRelease.
    protected override void resetBeforeSynchronousRelease(NpgsqlConnection connection)
    {
        RlsSessionVariableApplier.ResetBeforeSynchronousRelease(connection, _settingName, _options.Tenant?.Database, Logger);
    }

    protected override Task BeforeCloseAsync(NpgsqlConnection connection, CancellationToken token)
    {
        // #5881: never throws, so a failed reset cannot keep the connection from being disposed
        return RlsSessionVariableApplier.ResetBeforeReleaseAsync(connection, _settingName, _options.Tenant?.Database, Logger);
    }
}
