using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Resources;
using Marten.Schema;
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

/// <summary>
///     Marten's startup hosted service: create databases, migrate, roll partitions, assert, then seed.
///     The order and the failure handling come from JasperFx's <see cref="StoreActivator{TStore,TDatabase}" />
///     (jasperfx#1054); this supplies what each step means for Marten and the global migration lock.
/// </summary>
internal class MartenActivator: StoreActivator<IDocumentStore, IDatabase>, IGlobalLock<NpgsqlConnection>
{
    public MartenActivator(IDocumentStore store, ILogger<MartenActivator> logger): base(store, logger)
    {
        DocumentStore = store.As<DocumentStore>();
    }

    public DocumentStore DocumentStore { get; }

    protected override bool ShouldApplyChangesOnStartup => DocumentStore.Options.ShouldApplyChangesOnStartup;

    protected override bool ShouldAssertDatabaseMatchesConfigurationOnStartup =>
        DocumentStore.Options.ShouldAssertDatabaseMatchesConfigurationOnStartup;

    protected override ResourceMigrationFailureMode FailureMode => DocumentStore.Options.ResourceMigrationFailureMode;

    protected override IEnumerable<JasperFx.IInitialData<IDocumentStore>> InitialData => DocumentStore.Options.InitialData;

    // GH-3521 (jasperfx#543): only non-null in the multi-host test-harness divergence case.
    protected override string? ApplicationAssemblyReuseWarning => DocumentStore.Options.ApplicationAssemblyReuseWarning;

    /// <summary>
    ///     Poll for the apply-changes advisory lock until <see cref="StoreOptions.ApplyChangesLockTimeout" />
    ///     expires.
    /// </summary>
    /// <remarks>
    ///     #5529. Was a fixed 50 / 100 / 250ms ladder, so a store had ~400ms to win the lock. That covers the
    ///     multi-replica rolling deploy the lock was built for — the loser waits out a peer's already-finished
    ///     migration — but not more than one store sharing a database inside one host, where the loser has to
    ///     wait out a migration that is still running.
    ///     <para>
    ///     Still <c>pg_try_advisory_lock</c> in a loop rather than the blocking <c>pg_advisory_lock</c>, on
    ///     purpose: a blocking acquire ignores the cancellation token and cannot be bounded, so a peer that dies
    ///     mid-migration would hang startup instead of failing it.
    ///     </para>
    /// </remarks>
    public async Task<AttainLockResult> TryAttainLock(NpgsqlConnection conn, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + DocumentStore.Options.ApplyChangesLockTimeout;
        var delay = 50;

        while (true)
        {
            var result = await conn.TryGetGlobalLock(DocumentStore.Options.ApplyChangesLockId, cancellation: ct)
                .ConfigureAwait(false);

            // ShouldReconnect is the caller's business: Weasel opens a fresh connection and calls back in.
            if (result.Succeeded || result.ShouldReconnect)
                return result;

            if (DateTimeOffset.UtcNow >= deadline)
                return result;

            await Task.Delay(delay, ct).ConfigureAwait(false);

            // Backs off to a 250ms ceiling — the last rung of the original ladder. A migration holding the
            // lock runs for seconds, so polling faster than that only adds round trips.
            delay = Math.Min(delay * 2, 250);
        }
    }

    public Task ReleaseLock(NpgsqlConnection conn, CancellationToken ct = default)
    {
        return conn.ReleaseGlobalLock(DocumentStore.Options.ApplyChangesLockId, cancellation: ct);
    }

    protected override async Task CreateDatabasesAsync(CancellationToken token)
    {
        if (DocumentStore.Options.CreateDatabases != null)
        {
            var databaseGenerator = new DatabaseGenerator();
            await databaseGenerator
                .CreateDatabasesAsync(DocumentStore.Tenancy, DocumentStore.Options.CreateDatabases, token)
                .ConfigureAwait(false);
        }
    }

    protected override async ValueTask<IReadOnlyList<IDatabase>> BuildDatabasesAsync(CancellationToken token)
    {
        return await DocumentStore.Tenancy.BuildDatabases().ConfigureAwait(false);
    }

    protected override async Task ApplyChangesAsync(IDatabase database, CancellationToken token)
    {
        var postgresqlDatabase = (PostgresqlDatabase)database;

        // #4750: propagate the failure mode so Weasel's apply returns rather than throwing when it
        // can't attain the global migration lock (e.g. a replica that loses the lock race during a
        // multi-replica rolling deploy).
        postgresqlDatabase.ResourceMigrationFailureMode = FailureMode;

        await postgresqlDatabase
            .ApplyAllConfiguredChangesToDatabaseAsync(this, AutoCreate.CreateOrUpdate, ct: token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     #5093: roll every configured rolling-window RANGE partition forward and retire the aged ones.
    ///     The migration already provisions the leading edge, but migration never removes data, so the
    ///     retention drop has to be driven separately. Gated on the same opt-in as the migration itself,
    ///     and run BEFORE the assert on purpose: once the clock crosses a period boundary the database is
    ///     legitimately missing the new leading-edge partition, and asserting first would fail a
    ///     deployment over a difference this pass is about to close.
    /// </summary>
    protected override async Task AfterApplyingChangesAsync(IReadOnlyList<IDatabase> migrated, CancellationToken token)
    {
        await RollingPartitions
            .ApplyAsync(migrated.OfType<PostgresqlDatabase>(), Logger, rollForward: true, dropAged: true, token)
            .ConfigureAwait(false);
    }

    protected override Task AssertDatabaseMatchesConfigurationAsync(IDatabase database, CancellationToken token)
    {
        return database.AssertDatabaseMatchesConfigurationAsync(token);
    }

    protected override string DescribeDatabase(IDatabase database) => database.Identifier;
}
