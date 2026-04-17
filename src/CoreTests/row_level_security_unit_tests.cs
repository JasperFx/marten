using System;
using System.IO;
using System.Linq;
using Marten;
using Marten.Schema;
using Marten.Internal.Sessions;
using Marten.Internal.Sessions.Rls;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class row_level_security_unit_tests
{
    [Fact]
    public void can_enable_row_level_security_with_default_setting_name()
    {
        var options = new StoreOptions();

        #region sample_enable_row_level_security

        options.UseRowLevelSecurity();

        #endregion

        options.RlsTenantSessionSetting.ShouldBe("app.tenant_id");
    }

    [Fact]
    public void can_enable_row_level_security_with_custom_setting_name()
    {
        var options = new StoreOptions();

        #region sample_enable_row_level_security_custom_setting

        options.UseRowLevelSecurity("security.tenant");

        #endregion

        options.RlsTenantSessionSetting.ShouldBe("security.tenant");
    }

    [Fact]
    public void session_options_builds_rls_auto_closing_lifetime_when_enabled()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UseRowLevelSecurity();
        });

        var database = Substitute.For<IMartenDatabase>();
        var sessionOptions = SessionOptions.ForDatabase("tenant_blue", database);

        var lifetime = sessionOptions.Initialize(store, CommandRunnerMode.Transactional,
            new OpenTelemetryOptions { TrackConnections = TrackLevel.None });

        lifetime.ShouldBeOfType<RlsAutoClosingLifetime>();
    }

    [Fact]
    public void session_options_uses_plain_auto_closing_lifetime_when_disabled()
    {
        using var store = DocumentStore.For(opts => opts.Connection(ConnectionSource.ConnectionString));

        var database = Substitute.For<IMartenDatabase>();
        var sessionOptions = SessionOptions.ForDatabase("tenant_blue", database);

        var lifetime = sessionOptions.Initialize(store, CommandRunnerMode.Transactional,
            new OpenTelemetryOptions { TrackConnections = TrackLevel.None });

        lifetime.ShouldBeOfType<AutoClosingLifetime>();
        lifetime.ShouldNotBeOfType<RlsAutoClosingLifetime>();
    }

    [Fact]
    public void session_options_builds_rls_sticky_transactional_connection_when_sticky_enabled()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UseRowLevelSecurity();
            opts.UseStickyConnectionLifetimes = true;
        });

        var database = Substitute.For<IMartenDatabase>();
        var sessionOptions = SessionOptions.ForDatabase("tenant_blue", database);

        var lifetime = sessionOptions.Initialize(store, CommandRunnerMode.Transactional,
            new OpenTelemetryOptions { TrackConnections = TrackLevel.None });

        lifetime.ShouldBeOfType<RlsTransactionalConnection>();
    }

    [Fact]
    public void session_options_uses_plain_sticky_transactional_connection_when_rls_disabled()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.UseStickyConnectionLifetimes = true;
        });

        var database = Substitute.For<IMartenDatabase>();
        var sessionOptions = SessionOptions.ForDatabase("tenant_blue", database);

        var lifetime = sessionOptions.Initialize(store, CommandRunnerMode.Transactional,
            new OpenTelemetryOptions { TrackConnections = TrackLevel.None });

        lifetime.ShouldBeOfType<TransactionalConnection>();
        lifetime.ShouldNotBeOfType<RlsTransactionalConnection>();
    }

    [Fact]
    public void use_row_level_security_rejects_invalid_setting_name()
    {
        var options = new StoreOptions();

        Should.Throw<ArgumentException>(() => options.UseRowLevelSecurity("app.tenant_id'; drop table users; --"));
        Should.Throw<ArgumentException>(() => options.UseRowLevelSecurity("no_dot"));
        Should.Throw<ArgumentException>(() => options.UseRowLevelSecurity(""));
    }

    [Fact]
    public void rls_policy_schema_object_writes_expected_create_and_drop_sql()
    {
        var schemaObject = new RlsPolicySchemaObject(
            new PostgresqlObjectName("public", "mt_doc_target", SchemaUtils.IdentifierUsage.General),
            "app.tenant_id");

        var createWriter = new StringWriter();
        schemaObject.WriteCreateStatement(new PostgresqlMigrator(), createWriter);
        var createSql = createWriter.ToString();

        createSql.ShouldContain("ALTER TABLE public.mt_doc_target ENABLE ROW LEVEL SECURITY;");
        createSql.ShouldContain("ALTER TABLE public.mt_doc_target FORCE ROW LEVEL SECURITY;");
        createSql.ShouldContain("DROP POLICY IF EXISTS marten_tenant_isolation ON public.mt_doc_target;");
        createSql.ShouldContain("CREATE POLICY marten_tenant_isolation ON public.mt_doc_target");
        createSql.ShouldContain("USING (tenant_id = current_setting('app.tenant_id'))");
        createSql.ShouldContain("WITH CHECK (tenant_id = current_setting('app.tenant_id'));");

        var dropWriter = new StringWriter();
        schemaObject.WriteDropStatement(new PostgresqlMigrator(), dropWriter);
        var dropSql = dropWriter.ToString();

        dropSql.ShouldContain("DROP POLICY IF EXISTS marten_tenant_isolation ON public.mt_doc_target;");
        dropSql.ShouldContain("ALTER TABLE public.mt_doc_target NO FORCE ROW LEVEL SECURITY;");
        dropSql.ShouldContain("ALTER TABLE public.mt_doc_target DISABLE ROW LEVEL SECURITY;");
    }

    [Fact]
    public void document_schema_includes_rls_policy_when_enabled_for_conjoined_tenancy()
    {
        var options = new StoreOptions();
        options.UseRowLevelSecurity();
        options.Schema.For<Target>().MultiTenanted();

        var mapping = options.Storage.MappingFor(typeof(Target));
        var schema = new DocumentSchema(mapping);

        schema.Objects.Any(x => x is RlsPolicySchemaObject).ShouldBeTrue();
    }

    [Fact]
    public void document_schema_includes_rls_policy_object_when_disabled_for_conjoined_tenancy()
    {
        var options = new StoreOptions();
        options.Schema.For<Target>().MultiTenanted();

        var mapping = options.Storage.MappingFor(typeof(Target));
        var schema = new DocumentSchema(mapping);

        schema.Objects.Any(x => x is RlsPolicySchemaObject).ShouldBeTrue();
    }

    [Fact]
    public void mapping_without_override_inherits_store_rls_setting()
    {
        var options = new StoreOptions();
        options.UseRowLevelSecurity("app.tenant_id");
        options.Schema.For<Target>().MultiTenanted();

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeTrue();
        settingName.ShouldBe("app.tenant_id");
    }

    [Fact]
    public void mapping_disable_override_wins_even_when_store_enabled()
    {
        var options = new StoreOptions();

        #region sample_disable_row_level_security_per_mapping

        options.UseRowLevelSecurity();
        options.Schema.For<Target>().MultiTenanted().DisableRowLevelSecurity();

        #endregion

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeFalse();
        settingName.ShouldBeNull();
    }

    [Fact]
    public void mapping_enable_override_uses_explicit_setting()
    {
        var options = new StoreOptions();

        #region sample_use_row_level_security_per_mapping

        options.UseRowLevelSecurity();
        options.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity("app.org_id");

        #endregion

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeTrue();
        settingName.ShouldBe("app.org_id");
    }

    [Fact]
    public void mapping_enable_override_with_null_setting_falls_back_to_store_setting()
    {
        var options = new StoreOptions();
        options.UseRowLevelSecurity("security.tenant");
        options.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity();

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeTrue();
        settingName.ShouldBe("security.tenant");
    }

    [Fact]
    public void mapping_enable_override_with_null_setting_and_no_store_setting_falls_back_to_default()
    {
        var options = new StoreOptions();
        options.Schema.For<Target>().MultiTenanted().UseRowLevelSecurity();

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeTrue();
        settingName.ShouldBe("app.tenant_id");
    }

    [Fact]
    public void single_tenancy_resolves_disabled_regardless_of_override()
    {
        var options = new StoreOptions();
        options.UseRowLevelSecurity();
        options.Schema.For<Target>().UseRowLevelSecurity("app.org_id");

        var (enabled, settingName) = options.Storage.MappingFor(typeof(Target))
            .ResolveRowLevelSecurity();

        enabled.ShouldBeFalse();
        settingName.ShouldBeNull();
    }
}
