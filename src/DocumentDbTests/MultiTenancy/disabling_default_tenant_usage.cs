using JasperFx;
using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

public class disabling_default_tenant_usage : OneOffConfigurationsContext
{
    [Fact]
    public void get_exception_when_creating_session_with_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            using (var session = theStore.LightweightSession()) { }
        });
    }

    [Fact]
    public void get_exception_when_creating_query_session_with_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            using (var session = theStore.LightweightSession()) { }
        });
    }

    [Fact]
    public void get_exception_when_creating_session_with_default_tenant_and_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            using (var session = theStore.LightweightSession(StorageConstants.DefaultTenantId)) { }
        });
    }

    [Fact]
    public void get_exception_when_creating_query_session_with_default_tenant_and_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            using (var session = theStore.LightweightSession(StorageConstants.DefaultTenantId)) { }
        });
    }

    [Fact]
    public void get_exception_when_creating_session_with_default_tenant_session_options_and_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            var sessionOptions = new SessionOptions {TenantId = StorageConstants.DefaultTenantId};
            using (var session = theStore.LightweightSession(sessionOptions)) { }
        });
    }

    [Fact]
    public void get_exception_when_creating_query_session_with_default_tenant_session_options_and_default_tenant_usage_disabled()
    {
        StoreOptions(_ =>
        {
            _.Advanced.DefaultTenantUsageEnabled = false;
        });

        Should.Throw<DefaultTenantUsageDisabledException>(() =>
        {
            var sessionOptions = new SessionOptions {TenantId = StorageConstants.DefaultTenantId};
            using (var session = theStore.QuerySession(sessionOptions)) { }
        });
    }

}
