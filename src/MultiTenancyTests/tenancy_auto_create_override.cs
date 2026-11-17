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

namespace MultiTenancyTests;

/// <summary>
///     <see cref="MasterTableTenancyOptions" />'s <c>AutoCreate</c> is documented to "override the AutoCreate
///     setting for just the master tenancy table". It used to decide only WHETHER the master table was
///     migrated: the gate read <c>AutoCreate ?? StoreOptions.AutoCreateSchemaObjects</c>, and the apply then
///     ran under <c>StoreOptions.AutoCreateSchemaObjects</c> regardless. So the override was ignored for every
///     combination in which the store's own mode is not <c>None</c> (Weasel promotes an explicit <c>None</c>
///     apply to <c>CreateOrUpdate</c>, which is what made the documented store-<c>None</c> example work).
/// </summary>
/// <remarks>
///     The fixture for both facts is a master table in the shape Marten created before 8.37.3, which had no
///     <c>disabled</c> column — the upgrade every pre-8.37.3 master-table host makes.
/// </remarks>
public class master_table_tenancy_auto_create_override
{
    private const string SchemaName = "master_auto_create";

    [Fact]
    public async Task create_or_update_on_the_tenancy_migrates_the_master_table_when_the_store_is_create_only()
    {
        await createMasterTableWithoutDisabledColumnAsync();

        using var store = DocumentStore.For(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = SchemaName;
                x.AutoCreate = AutoCreate.CreateOrUpdate;
            });
        });

        var tenancy = (MasterTableTenancy)store.Options.Tenancy;

        await tenancy.BuildDatabases();

        (await disabledColumnExistsAsync()).ShouldBeTrue();

        // And the table is usable again: the tenant lookup reads the disabled column.
        await tenancy.AddDatabaseRecordAsync("tenant1", ConnectionSource.ConnectionString);
        (await tenancy.GetTenantAsync("tenant1")).TenantId.ShouldBe("tenant1");
    }

    [Fact]
    public async Task create_only_on_the_tenancy_is_not_overruled_by_a_create_or_update_store()
    {
        await createMasterTableWithoutDisabledColumnAsync();

        using var store = DocumentStore.For(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

            opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = SchemaName;
                x.AutoCreate = AutoCreate.CreateOnly;
            });
        });

        var tenancy = (MasterTableTenancy)store.Options.Tenancy;

        // CreateOnly creates what is missing and refuses to alter what exists, exactly as it does for a
        // document table under the store's own CreateOnly.
        await Should.ThrowAsync<SchemaMigrationException>(async () =>
        {
            await tenancy.BuildDatabases();
        });

        (await disabledColumnExistsAsync()).ShouldBeFalse();
    }

    private static async Task createMasterTableWithoutDisabledColumnAsync()
    {
        var token = TestContext.Current.CancellationToken;

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(token);
            await conn.DropSchemaAsync(SchemaName, token);
        }

        using (var store = DocumentStore.For(opts =>
               {
                   opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                   {
                       x.ConnectionString = ConnectionSource.ConnectionString;
                       x.SchemaName = SchemaName;
                   });
               }))
        {
            await ((MasterTableTenancy)store.Options.Tenancy).BuildDatabases();
        }

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(token);
            await conn.CreateCommand(
                    $"alter table {SchemaName}.{MasterTableTenancy.TenantTable.TableName} drop column {MasterTableTenancy.TenantTable.DisabledColumn}")
                .ExecuteNonQueryAsync(token);
        }

        (await disabledColumnExistsAsync()).ShouldBeFalse();
    }

    private static Task<bool> disabledColumnExistsAsync() =>
        TenancyAutoCreateOverride.ColumnExistsAsync(SchemaName, MasterTableTenancy.TenantTable.TableName,
            MasterTableTenancy.TenantTable.DisabledColumn);
}

/// <summary>
///     The sharded twin of <see cref="master_table_tenancy_auto_create_override" />:
///     <see cref="ShardedTenancyOptions.AutoCreate" /> is documented to "override the AutoCreate setting for
///     the master database tables", and its <c>maybeApplyChanges</c> had the same gate-only reading. The
///     fixture is a pool whose <c>mt_tenant_assignments</c> predates the #4607 <c>disabled</c> column.
/// </summary>
public class sharded_tenancy_auto_create_override
{
    private const string SchemaName = "sharded_auto_create";
    private const string AssignmentTable = "mt_tenant_assignments";

    [Fact]
    public async Task create_or_update_on_the_tenancy_migrates_the_pool_tables_when_the_store_is_create_only()
    {
        await createPoolTablesWithoutDisabledColumnAsync();

        using var store = DocumentStore.For(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = SchemaName;
                x.AutoCreate = AutoCreate.CreateOrUpdate;
            });
        });

        var tenancy = (ShardedTenancy)store.Options.Tenancy;

        await tenancy.BuildDatabases();

        (await disabledColumnExistsAsync()).ShouldBeTrue();

        // And the pool is usable again: the soft-delete listing reads the disabled column.
        (await tenancy.AllDisabledAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task create_only_on_the_tenancy_is_not_overruled_by_a_create_or_update_store()
    {
        await createPoolTablesWithoutDisabledColumnAsync();

        using var store = DocumentStore.For(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = SchemaName;
                x.AutoCreate = AutoCreate.CreateOnly;
            });
        });

        var tenancy = (ShardedTenancy)store.Options.Tenancy;

        await Should.ThrowAsync<SchemaMigrationException>(async () =>
        {
            await tenancy.BuildDatabases();
        });

        (await disabledColumnExistsAsync()).ShouldBeFalse();
    }

    private static async Task createPoolTablesWithoutDisabledColumnAsync()
    {
        var token = TestContext.Current.CancellationToken;

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(token);
            await conn.DropSchemaAsync(SchemaName, token);
        }

        using (var store = DocumentStore.For(opts =>
               {
                   opts.MultiTenantedWithShardedDatabases(x =>
                   {
                       x.ConnectionString = ConnectionSource.ConnectionString;
                       x.SchemaName = SchemaName;
                   });
               }))
        {
            await ((ShardedTenancy)store.Options.Tenancy).BuildDatabases();
        }

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(token);
            await conn.CreateCommand(
                    $"alter table {SchemaName}.{AssignmentTable} drop column {MartenTenantAssignmentTable.DisabledColumn}")
                .ExecuteNonQueryAsync(token);
        }

        (await disabledColumnExistsAsync()).ShouldBeFalse();
    }

    private static Task<bool> disabledColumnExistsAsync() =>
        TenancyAutoCreateOverride.ColumnExistsAsync(SchemaName, AssignmentTable,
            MartenTenantAssignmentTable.DisabledColumn);
}

internal static class TenancyAutoCreateOverride
{
    public static async Task<bool> ColumnExistsAsync(string schemaName, string tableName, string columnName)
    {
        var token = TestContext.Current.CancellationToken;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(token);

        var count = (long)(await conn.CreateCommand(
                $"select count(*) from information_schema.columns where table_schema = '{schemaName}' and table_name = '{tableName}' and column_name = '{columnName}'")
            .ExecuteScalarAsync(token))!;

        return count == 1;
    }
}
