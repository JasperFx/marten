#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

/// <summary>
/// #5684: Storage.PartitionPerTenant(table) partitions one of your own tables per tenant like Marten's document
/// tables, including the exemption from partition reconciliation that keeps a re-applied schema from rebuilding
/// the table through a _temp copy on a database that already has tenants.
/// </summary>
public class partition_extended_table_per_tenant: IAsyncLifetime
{
    private readonly string _schema = "pp_tenant_p" + Environment.ProcessId;
    private readonly string _partitionSchema = "pp_tenant_parts_p" + Environment.ProcessId;

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(_partitionSchema);
        await conn.DropSchemaAsync(_schema);
    }

    public async ValueTask DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(_partitionSchema);
        await conn.DropSchemaAsync(_schema);
    }

    private Table ledgerTable()
    {
        var table = new Table(new DbObjectName(_schema, "ledger_entries"));
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn<string>("tenant_id").NotNull();
        table.AddColumn<int>("amount");
        return table;
    }

    private DocumentStore buildStore()
    {
        return (DocumentStore)DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(_partitionSchema);

            #region sample_partition_extended_table_per_tenant

            var table = new Table(new DbObjectName(_schema, "ledger_entries"));
            table.AddColumn<Guid>("id").AsPrimaryKey();
            table.AddColumn<string>("tenant_id").NotNull();
            table.AddColumn<int>("amount");

            // One LIST partition per tenant, created as tenants are added,
            // and left alone by later schema migrations
            opts.Storage.PartitionPerTenant(table);

            #endregion
        });
    }

    [Fact]
    public async Task reapplying_the_schema_over_existing_tenants_does_not_rebuild_the_table()
    {
        // First deploy: the table, two tenants (so two partitions), and rows in both
        await using (var store = buildStore())
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "acme", "globex");

            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();
            await conn.CreateCommand(
                    $"insert into {_schema}.ledger_entries (id, tenant_id, amount) values (gen_random_uuid(), 'acme', 1), (gen_random_uuid(), 'globex', 2)")
                .ExecuteNonQueryAsync();

            (await partitionCount(conn)).ShouldBe(2);
        }

        // Second deploy, nothing changed: no difference, so no _temp copy and no 23514
        await using (var store = buildStore())
        {
            var migration = await store.Storage.CreateMigrationAsync();
            migration.Difference.ShouldBe(SchemaPatchDifference.None);

            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            // A tenant added later gets its partition in this table too
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "initech");

            await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            await conn.OpenAsync();
            (await partitionCount(conn)).ShouldBe(3);

            var rows = (long)(await conn.CreateCommand($"select count(*) from {_schema}.ledger_entries")
                .ExecuteScalarAsync())!;
            rows.ShouldBe(2);
        }
    }

    private async Task<long> partitionCount(NpgsqlConnection conn)
    {
        return (long)(await conn.CreateCommand($@"
select count(*) from pg_inherits i
  join pg_class parent on parent.oid = i.inhparent
  join pg_namespace n on n.oid = parent.relnamespace
 where n.nspname = '{_schema}' and parent.relname = 'ledger_entries'").ExecuteScalarAsync())!;
    }

    [Fact]
    public void the_table_is_partitioned_like_the_document_tables()
    {
        var table = ledgerTable();

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(_partitionSchema);
            opts.Storage.PartitionPerTenant(table);
        });

        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.ShouldBe(["tenant_id"]);
        partitioning.PartitionManager.ShouldBeSameAs(store.Options.TenantPartitions!.Partitions);

        // The flag that keeps the schema diff away from the per-tenant partitions
        table.IgnorePartitionsInMigration.ShouldBeTrue();

        // PostgreSQL requires the partition key in the primary key
        table.PrimaryKeyColumns.ShouldBe(["id", "tenant_id"], ignoreOrder: true);

        store.Options.Storage.ExtendedSchemaObjects.ShouldContain(table);
    }

    [Fact]
    public void a_store_without_marten_managed_tenant_partitioning_refuses()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = _schema;
                opts.Storage.PartitionPerTenant(ledgerTable());
            });
        });

        ex.Message.ShouldContain($"{_schema}.ledger_entries");
        ex.Message.ShouldContain("PartitionMultiTenantedDocumentsUsingMartenManagement");
    }

    [Fact]
    public void a_table_without_a_tenant_id_column_is_refused()
    {
        var table = new Table(new DbObjectName(_schema, "no_tenant"));
        table.AddColumn<Guid>("id").AsPrimaryKey();

        var opts = new StoreOptions();
        Should.Throw<ArgumentException>(() => opts.Storage.PartitionPerTenant(table))
            .Message.ShouldContain("tenant_id");
    }
}
