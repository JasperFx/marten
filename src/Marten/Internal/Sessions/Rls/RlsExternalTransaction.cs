#nullable enable
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions.Rls;

internal sealed class RlsExternalTransaction: ExternalTransaction
{
    private readonly string _tenantId;
    private readonly string _settingName;
    private bool _applied;

    public RlsExternalTransaction(SessionOptions options, string tenantId, string settingName): base(options)
    {
        _tenantId = tenantId;
        _settingName = settingName;
    }

    public override void Apply(NpgsqlCommand command)
    {
        EnsureApplied();
        base.Apply(command);
    }

    public override async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
    {
        await EnsureAppliedAsync(token).ConfigureAwait(false);
        await base.ApplyAsync(command, token).ConfigureAwait(false);
    }

    public override void Apply(NpgsqlBatch batch)
    {
        EnsureApplied();
        base.Apply(batch);
    }

    public override async Task ApplyAsync(NpgsqlBatch batch, CancellationToken token)
    {
        await EnsureAppliedAsync(token).ConfigureAwait(false);
        await base.ApplyAsync(batch, token).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        if (_applied)
        {
            RlsSessionVariableApplier.Reset(Connection, _settingName);
        }
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_applied)
        {
            await RlsSessionVariableApplier.ResetAsync(Connection, _settingName).ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureApplied()
    {
        if (_applied || Connection.State != ConnectionState.Open) return;
        RlsSessionVariableApplier.Apply(Connection, _settingName, _tenantId);
        _applied = true;
    }

    private async ValueTask EnsureAppliedAsync(CancellationToken token)
    {
        if (_applied || Connection.State != ConnectionState.Open) return;
        await RlsSessionVariableApplier.ApplyAsync(Connection, _settingName, _tenantId, token).ConfigureAwait(false);
        _applied = true;
    }
}
