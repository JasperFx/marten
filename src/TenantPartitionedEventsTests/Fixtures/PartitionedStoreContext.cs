#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Fixtures;

/// <summary>
/// Per-test-class store lifecycle for tests that need their OWN
/// <see cref="DocumentStore"/> under <c>UseTenantPartitionedEvents</c> — the
/// cases where registering file-local projections or flipping a store-level
/// flag on the shared <see cref="PartitionedFixtureBase"/> fixtures would
/// pollute sibling tests. Absorbs the copy-paste template that used to be
/// re-declared per file: drop a fresh pid+guid schema, build the store with
/// the common partitioned-tenancy config, ensure event storage, dispose.
/// </summary>
/// <remarks>
/// Subclasses override <see cref="ConfigureStore"/> for file-local event
/// types, projections, and flag deviations — it runs AFTER the common config
/// lines so it can override any of them. Prefer the shared collection
/// fixtures when a test doesn't need store-level customization; per-test
/// isolation there is by unique tenant id, which is much cheaper than a
/// schema per class.
/// </remarks>
public abstract class PartitionedStoreContext: IAsyncLifetime
{
    protected DocumentStore Store { get; private set; } = null!;
    protected string Schema { get; private set; } = null!;

    /// <summary>
    /// Short lowercase schema tag, e.g. <c>tp_dlq</c>. Keep it terse — the
    /// generated name <c>{prefix}_{pid}_{guid}</c> is truncated to 32 chars
    /// and must stay unique-per-run after truncation.
    /// </summary>
    protected abstract string SchemaPrefix { get; }

    /// <summary>
    /// File-local store configuration. Runs after the common partitioned
    /// config (Conjoined + UseTenantPartitionedEvents +
    /// QuickWithServerTimestamps + AllDocumentsAreMultiTenanted), so it can
    /// override any of those lines.
    /// </summary>
    protected abstract void ConfigureStore(StoreOptions opts);

    /// <summary>
    /// Lowercase, hyphen-free, leading-digit-safe (#4567), under PostgreSQL's
    /// identifier limit with room for Marten's table suffixes. Override for
    /// tests pinned to a stable schema across store rebuilds.
    /// </summary>
    protected virtual string BuildSchemaName()
    {
        var name = $"{SchemaPrefix}_{Environment.ProcessId}_{Guid.NewGuid():N}";
        return name.Length <= 32 ? name : name.Substring(0, 32);
    }

    /// <summary>
    /// Override to false for tests that intentionally build against whatever
    /// schema state is already present.
    /// </summary>
    protected virtual bool DropSchemaOnInitialize => true;

    /// <summary>
    /// Override to false for tests that must observe the store BEFORE event
    /// storage exists (e.g. first-touch schema creation behavior).
    /// </summary>
    protected virtual bool EnsureStorageOnInitialize => true;

    public virtual async ValueTask InitializeAsync()
    {
        await BuildFreshStoreAsync();
    }

    /// <summary>
    /// Disposes any current store, mints a fresh schema name, and rebuilds.
    /// Re-entrant on purpose: concurrency-regression tests re-run the whole
    /// lifecycle per attempt.
    /// </summary>
    protected async Task BuildFreshStoreAsync()
    {
        Store?.Dispose();

        Schema = BuildSchemaName();

        if (DropSchemaOnInitialize)
        {
            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(Schema); } catch { }
        }

        Store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = Schema;

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            ConfigureStore(opts);
        });

        if (EnsureStorageOnInitialize)
        {
            await Store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        Store?.Dispose();
        return default;
    }
}
