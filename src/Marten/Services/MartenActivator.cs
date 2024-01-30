using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Services;

internal class MartenActivator<T>: MartenActivator where T : IDocumentStore
{
    public MartenActivator(T store, ILogger<MartenActivator> logger): base(store, logger)
    {
    }
}

internal class MartenActivator: IHostedService, IGlobalLock<NpgsqlConnection>
{
    private readonly ILogger<MartenActivator> _logger;

    public MartenActivator(IDocumentStore store, ILogger<MartenActivator> logger)
    {
        _logger = logger;
        Store = store.As<DocumentStore>();
    }

    public DocumentStore Store { get; }

    public async Task<AttainLockResult> TryAttainLock(NpgsqlConnection conn, CancellationToken ct = default)
    {
        var result = await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId, cancellation: ct)
            .ConfigureAwait(false);

        if (result.Succeeded || result.ShouldReconnect)
            return result;

        await Task.Delay(50, ct).ConfigureAwait(false);
        result = await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId, cancellation: ct).ConfigureAwait(false);

        if (result.Succeeded || result.ShouldReconnect)
            return result;

        await Task.Delay(100, ct).ConfigureAwait(false);
        result = await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId, cancellation: ct).ConfigureAwait(false);

        if (result.Succeeded || result.ShouldReconnect)
            return result;

        await Task.Delay(250, ct).ConfigureAwait(false);
        result = await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId, cancellation: ct).ConfigureAwait(false);

        return result;
    }

    public Task ReleaseLock(NpgsqlConnection conn, CancellationToken ct = default)
    {
        return conn.ReleaseGlobalLock(Store.Options.ApplyChangesLockId, cancellation: ct);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Store.Options.CreateDatabases != null)
        {
            var databaseGenerator = new DatabaseGenerator();
            await databaseGenerator
                .CreateDatabasesAsync(Store.Tenancy, Store.Options.CreateDatabases, cancellationToken)
                .ConfigureAwait(false);
        }

        var databases = await Store.Tenancy.BuildDatabases().ConfigureAwait(false);

        if (Store.Options.ShouldApplyChangesOnStartup)
        {
            foreach (PostgresqlDatabase database in databases)
            {
                await database
                    .ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate, ct: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (Store.Options.ShouldAssertDatabaseMatchesConfigurationOnStartup)
        {
            foreach (var database in databases)
            {
                await database.AssertDatabaseMatchesConfigurationAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var initialData in Store.Options.InitialData)
        {
            _logger.LogInformation("Applying initial data {InitialData}", initialData);
            await initialData.Populate(Store, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
