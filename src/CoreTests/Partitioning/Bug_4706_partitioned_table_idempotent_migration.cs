#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests.Partitioning;

public class Bug4706Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #4706 — re-applying schema to a Marten-managed ByList tenant-partitioned document table over
/// existing data must be idempotent. The report: on every startup the partitioned doc table is
/// diffed as "needs rebuild" (CREATE _temp -> INSERT ... SELECT -> drop/rename) even when nothing
/// changed, and the copy fails with 23514 (no partition for row) because the rebuilt table's
/// per-tenant LIST partitions aren't recreated before the copy. Suspected trigger: StartIndexesByTenantId.
/// </summary>
public class Bug_4706_partitioned_table_idempotent_migration: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _schema = "bug4706_p" + Environment.ProcessId;
    private readonly string _partitionSchema = "bug4706_tenants_p" + Environment.ProcessId;

    public Bug_4706_partitioned_table_idempotent_migration(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_partitionSchema); } catch { }
        try { await conn.DropSchemaAsync(_schema); } catch { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildStore() => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = _schema;
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(_partitionSchema);

        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;

        // Match the reporter's per-document form exactly: explicit MultiTenantedWithPartitioning(ByList)
        // + StartIndexesByTenantId + a computed index.
        opts.Schema.For<Bug4706Doc>()
            .MultiTenantedWithPartitioning(x => x.ByList())
            .Index(x => x.Name)
            .StartIndexesByTenantId();
    });

    [Fact]
    public async Task reapply_over_existing_tenant_data_is_idempotent()
    {
        // 1. First deploy: create schema, register tenants (=> per-tenant LIST partitions), store data.
        await using (var store = BuildStore())
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "acme", "globex");

            foreach (var tenant in new[] { "acme", "globex" })
            {
                await using var session = store.LightweightSession(tenant);
                session.Store(new Bug4706Doc { Id = Guid.NewGuid(), Name = "n-" + tenant });
                await session.SaveChangesAsync();
            }
        }

        // 2. Second deploy (nothing changed): the diff must be None — no destructive rebuild.
        await using (var store = BuildStore())
        {
            var migration = await store.Storage.CreateMigrationAsync();
            _output.WriteLine($"Difference = {migration.Difference}");

            migration.Difference.ShouldBe(SchemaPatchDifference.None,
                "Re-applying an unchanged ByList tenant-partitioned doc table must not diff as a change");

            // And a real re-apply must not throw 23514 / rebuild-copy failure.
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }
    }
}
