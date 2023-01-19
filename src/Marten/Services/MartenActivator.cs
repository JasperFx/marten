using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Storage;
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

    public async Task<bool> TryAttainLock(NpgsqlConnection conn)
    {
        if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false))
        {
            return true;
        }

        await Task.Delay(50).ConfigureAwait(false);
        if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false))
        {
            return true;
        }

        await Task.Delay(100).ConfigureAwait(false);
        if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false))
        {
            return true;
        }

        await Task.Delay(250).ConfigureAwait(false);
        if (await conn.TryGetGlobalLock(Store.Options.ApplyChangesLockId).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    public Task ReleaseLock(NpgsqlConnection conn)
    {
        return conn.ReleaseGlobalLock(Store.Options.ApplyChangesLockId);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Store.Options.CreateDatabases != null)
        {
            var databaseGenerator = new DatabaseGenerator();
            await databaseGenerator.CreateDatabasesAsync(Store.Tenancy, Store.Options.CreateDatabases).ConfigureAwait(false);
        }

        if (Store.Options.ShouldApplyChangesOnStartup)
        {
            var databases = Store.Tenancy.BuildDatabases().ConfigureAwait(false);
            foreach (PostgresqlDatabase database in await databases)
                await database.ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate)
                    .ConfigureAwait(false);
        }

        if (Store.Options.ShouldAssertDatabaseMatchesConfigurationOnStartup)
        {
            var databases = Store.Tenancy.BuildDatabases().ConfigureAwait(false);
            foreach (var database in await databases)
                await database.AssertDatabaseMatchesConfigurationAsync().ConfigureAwait(false);
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
