using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Acceptance
{
    [Collection("multitenancy")]
    public class disabling_default_tenant_usage : OneOffConfigurationsContext
    {
        [Fact]
        public void get_exception_when_creating_session_with_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.OpenSession()) { }
            });
        }

        [Fact]
        public void get_exception_when_creating_query_session_with_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.QuerySession()) { }
            });
        }

        [Fact]
        public void get_exception_when_creating_session_with_default_tenant_and_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.OpenSession(Tenancy.DefaultTenantId)) { }
            });
        }

        [Fact]
        public void get_exception_when_creating_query_session_with_default_tenant_and_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.QuerySession(Tenancy.DefaultTenantId)) { }
            });
        }

        [Fact]
        public void get_exception_when_creating_session_with_default_tenant_session_options_and_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                var sessionOptions = new SessionOptions {TenantId = Tenancy.DefaultTenantId};
                using (var session = theStore.OpenSession(sessionOptions)) { }
            });
        }

        [Fact]
        public void get_exception_when_creating_query_session_with_default_tenant_session_options_and_default_tenant_usage_disabled()
        {
            StoreOptions(_ =>
            {
                _.DefaultTenantUsageEnabled = false;
            });

            Exception<DefaultTenantUsageDisabledException>.ShouldBeThrownBy(() =>
            {
                var sessionOptions = new SessionOptions {TenantId = Tenancy.DefaultTenantId};
                using (var session = theStore.QuerySession(sessionOptions)) { }
            });
        }

        public disabling_default_tenant_usage() : base("multitenancy")
        {
        }
    }
}
