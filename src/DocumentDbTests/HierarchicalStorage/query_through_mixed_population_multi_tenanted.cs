using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class query_through_mixed_population_multi_tenanted: OneOffConfigurationsContext, IAsyncLifetime
{

    public query_through_mixed_population_multi_tenanted()
    {
        StoreOptions(
            _ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Schema.For<User>().AddSubClass<SuperUser>().AddSubClass<AdminUser>().Duplicate(x => x.UserName);
            });


    }

    public async Task InitializeAsync()
    {
        await using var session = theStore.LightweightSession("tenant_1");
        session.Store(new User(), new AdminUser());
        await session.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public void query_tenanted_data_with_any_tenant_predicate()
    {
        using var session = theStore.QuerySession();
        var users = session.Query<AdminUser>().Where(u => LinqExtensions.AnyTenant<AdminUser>(u)).ToArray();
        users.Length.ShouldBeGreaterThan(0);
    }
}
