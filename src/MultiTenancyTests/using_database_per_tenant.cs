using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace MultiTenancyTests;

public class using_database_per_tenant: IAsyncLifetime
{
    private readonly ITestOutputHelper _testOutputHelper;
    private DocumentStore _theStore;

    public using_database_per_tenant(
        ITestOutputHelper testOutputHelper
    ) => _testOutputHelper = testOutputHelper;

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
    public async Task should_not_meaning_ful_exception_when_tenant_id_is_missing()
    {
        Should.Throw<ArgumentNullException>(() => _theStore.LightweightSession());
    }


    public async Task DisposeAsync() => await _theStore.DisposeAsync();
}
