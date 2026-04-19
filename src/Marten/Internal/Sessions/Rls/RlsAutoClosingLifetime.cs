#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

internal sealed class RlsAutoClosingLifetime: AutoClosingLifetime
{
    private readonly SessionOptions _options;
    private readonly string _tenantId;
    private readonly string _settingName;

    public RlsAutoClosingLifetime(SessionOptions options, StoreOptions storeOptions, string tenantId, string settingName)
        : base(options, storeOptions)
    {
        _options = options;
        _tenantId = tenantId;
        _settingName = settingName;
    }

    protected override void AfterOpened(NpgsqlConnection connection)
    {
        RlsSessionVariableApplier.Apply(connection, _settingName, _tenantId);
    }

    protected override Task AfterOpenedAsync(NpgsqlConnection connection, CancellationToken token)
    {
        return RlsSessionVariableApplier.ApplyAsync(connection, _settingName, _tenantId, token);
    }

    protected override TransactionalConnection CreateStickyConnection()
    {
        return _options.Mode == CommandRunnerMode.ReadOnly
            ? new RlsReadOnlyTransactionalConnection(_options, _tenantId, _settingName) { Logger = Logger, CommandTimeout = CommandTimeout }
            : new RlsTransactionalConnection(_options, _tenantId, _settingName) { Logger = Logger, CommandTimeout = CommandTimeout };
    }
}
