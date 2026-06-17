using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
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
            var failureMode = Store.Options.ResourceMigrationFailureMode;
            foreach (PostgresqlDatabase database in databases)
            {
                // #4750: propagate the failure mode so Weasel's apply returns rather than throwing when it
                // can't attain the global migration lock (e.g. a replica that loses the lock race during a
                // multi-replica rolling deploy).
                database.ResourceMigrationFailureMode = failureMode;

                try
                {
                    await database
                        .ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate, ct: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (failureMode == ResourceMigrationFailureMode.ContinueOnFailures)
                {
                    // Belt-and-suspenders for migration failures of any kind (not just the lock timeout):
                    // log and keep starting up rather than crash-looping. FailFast (default) rethrows.
                    _logger.LogError(e,
                        "Failed to apply configured database changes to {Database} at startup. Continuing startup anyway because ResourceMigrationFailureMode is ContinueOnFailures.",
                        database.Identifier);
                }
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
