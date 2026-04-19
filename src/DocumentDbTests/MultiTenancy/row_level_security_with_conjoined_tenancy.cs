using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

public class row_level_security_with_conjoined_tenancy: OneOffConfigurationsContext
{
    public row_level_security_with_conjoined_tenancy()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _schemaName = $"{GetType().Name}_{suffix}".ToLowerInvariant();
    }

    [Fact]
    public async Task applies_rls_policy_during_schema_migration()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;
        theStore.Options.DatabaseSchemaName.ShouldBe(_schemaName);
        tableName.Schema.ShouldBe(_schemaName);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select count(*) from pg_policy p " +
            "join pg_class c on p.polrelid = c.oid " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table and p.polname = 'marten_tenant_isolation'";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);

        var policyCount = (long)(await cmd.ExecuteScalarAsync());
        policyCount.ShouldBe(1);
    }

    [Fact]
    public async Task sets_session_tenant_setting_for_auto_closing_lifetime()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await using var session = theStore.QuerySession("tenant_red");
        var value = (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single();

        value.ShouldBe("tenant_red");
    }

    [Fact]
    public async Task sets_session_tenant_setting_for_sticky_connection_lifetime()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
            opts.UseStickyConnectionLifetimes = true;
        });

        await using var session = theStore.QuerySession("tenant_blue");
        var value = (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single();

        value.ShouldBe("tenant_blue");
    }

    [Fact]
    public async Task sets_session_tenant_setting_for_caller_supplied_transaction()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var sessionOptions = SessionOptions.ForTransaction(tx);
        sessionOptions.TenantId = "tenant_violet";

        await using var session = theStore.QuerySession(sessionOptions);
        var value = (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single();

        value.ShouldBe("tenant_violet");
    }

    [Fact]
    public async Task bulk_insert_succeeds_under_rls_for_conjoined_tenant()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targets = new[] { Target.Random(), Target.Random(), Target.Random() };
        await theStore.BulkInsertAsync("tenant_magenta", targets);

        await using var session = theStore.QuerySession("tenant_magenta");
        var ids = (await session.Query<Target>().ToListAsync()).Select(x => x.Id).OrderBy(x => x).ToList();
        ids.ShouldBe(targets.Select(x => x.Id).OrderBy(x => x).ToList());
    }

    [Fact]
    public async Task sets_session_tenant_setting_for_ambient_dot_net_transaction()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        var sessionOptions = SessionOptions.ForCurrentTransaction();
        sessionOptions.TenantId = "tenant_cyan";

        await using (var session = theStore.QuerySession(sessionOptions))
        {
            var value = (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single();
            value.ShouldBe("tenant_cyan");
        }

        scope.Complete();
    }

    [Fact]
    public async Task does_not_set_tenant_setting_when_rls_is_disabled()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
        });

        await using var session = theStore.QuerySession("tenant_green");
        var value = (await session.QueryAsync<string>("select current_setting('app.tenant_id', true)")).Single();

        value.ShouldBeNull();
    }

    [Fact]
    public async Task applies_rls_flags_and_policy_to_document_table()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select c.relrowsecurity, c.relforcerowsecurity from pg_class c " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetBoolean(0).ShouldBeTrue();
        reader.GetBoolean(1).ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task postgres_blocks_cross_tenant_reads_when_rls_enabled(bool useStickyConnectionLifetimes)
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
            opts.UseStickyConnectionLifetimes = useStickyConnectionLifetimes;
        });

        var targetA = Target.Random();
        var targetB = Target.Random();

        await using (var sessionA = theStore.LightweightSession("tenant_a"))
        {
            sessionA.Store(targetA);
            await sessionA.SaveChangesAsync();
        }

        await using (var sessionB = theStore.LightweightSession("tenant_b"))
        {
            sessionB.Store(targetB);
            await sessionB.SaveChangesAsync();
        }

        // Marten-layer check: its own conjoined-tenancy filter would satisfy this even without RLS,
        // but it still verifies the tenant setting does not break regular reads.
        await using (var sessionA = theStore.QuerySession("tenant_a"))
        {
            var rows = await sessionA.Query<Target>().ToListAsync();
            rows.Select(x => x.Id).ShouldBe([targetA.Id]);
        }

        // PostgreSQL-layer check: prove it's the RLS policy (not Marten) doing the filtering.
        // Requires a non-superuser role because superusers bypass RLS regardless of FORCE.
        var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using (var setup = conn.CreateCommand())
        {
            setup.CommandText = $@"
                DO $$ BEGIN
                  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'marten_rls_probe') THEN
                    CREATE ROLE marten_rls_probe;
                  END IF;
                END $$;
                GRANT USAGE ON SCHEMA {tableName.Schema} TO marten_rls_probe;
                GRANT SELECT ON {tableName.Schema}.{tableName.Name} TO marten_rls_probe;
                SET ROLE marten_rls_probe;";
            await setup.ExecuteNonQueryAsync();
        }

        await AssertVisibleRowCount(conn, tableName.QualifiedName, "tenant_a", 1);
        await AssertVisibleRowCount(conn, tableName.QualifiedName, "tenant_b", 1);
    }

    private static async Task AssertVisibleRowCount(NpgsqlConnection conn, string qualifiedTable, string tenantId, long expected)
    {
        await using var setTenant = conn.CreateCommand();
        setTenant.CommandText = "select set_config('app.tenant_id', @tenant, false)";
        setTenant.Parameters.AddWithValue("tenant", tenantId);
        await setTenant.ExecuteNonQueryAsync();

        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = $"select count(*) from {qualifiedTable}";
        var actual = (long)await selectCmd.ExecuteScalarAsync();
        actual.ShouldBe(expected, $"tenant='{tenantId}'");
    }

    [Fact]
    public async Task reapplies_policy_when_setting_name_changes()
    {
        using var initialStore = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await initialStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var updatedStore = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity("security.tenant");
        });

        await updatedStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = updatedStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select pg_get_expr(p.polqual, p.polrelid), pg_get_expr(p.polwithcheck, p.polrelid) " +
            "from pg_policy p " +
            "join pg_class c on p.polrelid = c.oid " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table and p.polname = 'marten_tenant_isolation'";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();

        var usingExpression = reader.GetString(0);
        var checkExpression = reader.GetString(1);

        usingExpression.ShouldContain("current_setting('security.tenant'");
        usingExpression.ShouldNotContain("current_setting('app.tenant_id'");
        checkExpression.ShouldContain("current_setting('security.tenant'");
        checkExpression.ShouldNotContain("current_setting('app.tenant_id'");
    }

    [Fact]
    public async Task removes_rls_policy_when_configuration_disables_row_level_security()
    {
        using var storeWithRls = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await storeWithRls.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var storeWithoutRls = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
        });

        await storeWithoutRls.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = storeWithoutRls.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var policyCmd = conn.CreateCommand();
        policyCmd.CommandText =
            "select count(*) from pg_policy p " +
            "join pg_class c on p.polrelid = c.oid " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table and p.polname = 'marten_tenant_isolation'";
        policyCmd.Parameters.AddWithValue("schema", tableName.Schema);
        policyCmd.Parameters.AddWithValue("table", tableName.Name);

        var policyCount = (long)(await policyCmd.ExecuteScalarAsync());
        policyCount.ShouldBe(0);

        await using var flagsCmd = conn.CreateCommand();
        flagsCmd.CommandText =
            "select c.relrowsecurity, c.relforcerowsecurity from pg_class c " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table";
        flagsCmd.Parameters.AddWithValue("schema", tableName.Schema);
        flagsCmd.Parameters.AddWithValue("table", tableName.Name);

        await using var reader = await flagsCmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetBoolean(0).ShouldBeFalse();
        reader.GetBoolean(1).ShouldBeFalse();
    }

    [Fact]
    public async Task mapping_opt_out_excludes_only_that_table_when_store_rls_enabled()
    {
        StoreOptions(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted();
            opts.Schema.For<User>().MultiTenanted().DisableRowLevelSecurity();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targetTable = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;
        var userTable = theStore.Options.Storage.MappingFor(typeof(User)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        (await PolicyCountAsync(conn, targetTable)).ShouldBe(1);
        (await PolicyCountAsync(conn, userTable)).ShouldBe(0);

        var (targetRls, targetForce) = await ReadRlsFlagsAsync(conn, targetTable);
        targetRls.ShouldBeTrue();
        targetForce.ShouldBeTrue();

        var (userRls, userForce) = await ReadRlsFlagsAsync(conn, userTable);
        userRls.ShouldBeFalse();
        userForce.ShouldBeFalse();
    }

    [Fact]
    public async Task mapping_custom_setting_writes_policy_with_that_setting()
    {
        StoreOptions(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted();
            opts.Schema.For<User>().MultiTenanted().UseRowLevelSecurity("app.org_id");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targetTable = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;
        var userTable = theStore.Options.Storage.MappingFor(typeof(User)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        (await ReadPolicyExpressionAsync(conn, targetTable)).ShouldContain("current_setting('app.tenant_id'");
        (await ReadPolicyExpressionAsync(conn, userTable)).ShouldContain("current_setting('app.org_id'");
    }

    [Fact]
    public async Task mapping_opt_out_drops_previously_applied_policy_on_next_migration()
    {
        using var initialStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted();
        });

        await initialStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var updatedStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted().DisableRowLevelSecurity();
        });

        await updatedStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = updatedStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        (await PolicyCountAsync(conn, tableName)).ShouldBe(0);

        var (rls, force) = await ReadRlsFlagsAsync(conn, tableName);
        rls.ShouldBeFalse();
        force.ShouldBeFalse();
    }

    [Fact]
    public async Task global_setting_switches_to_table_specific_setting_on_next_migration()
    {
        using var initialStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted();
        });

        await initialStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var updatedStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity("app.org_id");
        });

        await updatedStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = updatedStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var expression = await ReadPolicyExpressionAsync(conn, tableName);
        expression.ShouldContain("current_setting('app.org_id'");
        expression.ShouldNotContain("current_setting('app.tenant_id'");
    }

    [Fact]
    public async Task table_specific_setting_reverts_to_global_setting_when_override_removed()
    {
        using var initialStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity("app.org_id");
        });

        await initialStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var updatedStore = SeparateStore(opts =>
        {
            opts.UseRowLevelSecurity();
            opts.Schema.For<Target>().MultiTenanted();
        });

        await updatedStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = updatedStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var expression = await ReadPolicyExpressionAsync(conn, tableName);
        expression.ShouldContain("current_setting('app.tenant_id'");
        expression.ShouldNotContain("current_setting('app.org_id'");
    }

    [Fact]
    public async Task mapping_level_opt_in_creates_policy_when_store_rls_is_off()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity("app.org_id");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tableName = theStore.Options.Storage.MappingFor(typeof(Target)).TableName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        (await PolicyCountAsync(conn, tableName)).ShouldBe(1);

        var (rls, force) = await ReadRlsFlagsAsync(conn, tableName);
        rls.ShouldBeTrue();
        force.ShouldBeTrue();

        (await ReadPolicyExpressionAsync(conn, tableName)).ShouldContain("current_setting('app.org_id'");
    }

    private static async Task<long> PolicyCountAsync(NpgsqlConnection conn, Weasel.Core.DbObjectName tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select count(*) from pg_policy p " +
            "join pg_class c on p.polrelid = c.oid " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table and p.polname = 'marten_tenant_isolation'";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);
        return (long)await cmd.ExecuteScalarAsync();
    }

    private static async Task<(bool Rls, bool Force)> ReadRlsFlagsAsync(NpgsqlConnection conn, Weasel.Core.DbObjectName tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select c.relrowsecurity, c.relforcerowsecurity from pg_class c " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<string> ReadPolicyExpressionAsync(NpgsqlConnection conn, Weasel.Core.DbObjectName tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select pg_get_expr(p.polqual, p.polrelid) from pg_policy p " +
            "join pg_class c on p.polrelid = c.oid " +
            "join pg_namespace n on c.relnamespace = n.oid " +
            "where n.nspname = @schema and c.relname = @table and p.polname = 'marten_tenant_isolation'";
        cmd.Parameters.AddWithValue("schema", tableName.Schema);
        cmd.Parameters.AddWithValue("table", tableName.Name);
        var result = await cmd.ExecuteScalarAsync();
        return (string)result;
    }
}
