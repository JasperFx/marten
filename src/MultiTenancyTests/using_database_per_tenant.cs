using System;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;

namespace MultiTenancyTests;

public class using_database_per_tenant: IAsyncLifetime
{
    private DocumentStore _theStore;

    public async Task InitializeAsync()
    {
        _theStore = DocumentStore.For(
            options =>
            {
                options.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString);
            }
        );
    }

    [Fact]
    public void should_not_meaning_ful_exception_when_tenant_id_is_missing()
    {
        Should.Throw<DefaultTenantUsageDisabledException>(() => _theStore.LightweightSession());
    }


    public async Task DisposeAsync() => await _theStore.DisposeAsync();
}
