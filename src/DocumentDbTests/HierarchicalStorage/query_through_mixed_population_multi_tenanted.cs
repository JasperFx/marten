using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class query_through_mixed_population_multi_tenanted: OneOffConfigurationsContext
{

    public query_through_mixed_population_multi_tenanted()
    {
        StoreOptions(
            _ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Schema.For<User>().AddSubClass<SuperUser>().AddSubClass<AdminUser>().Duplicate(x => x.UserName);
            });

        loadData();
    }

    private void loadData()
    {
        using (var session = theStore.OpenSession("tenant_1"))
        {
            session.Store(new User(), new AdminUser());
            session.SaveChanges();
        }
    }

    [Fact]
    public void query_tenanted_data_with_any_tenant_predicate()
    {
        using (var session = theStore.OpenSession())
        {
            var users = session.Query<AdminUser>().Where(u => LinqExtensions.AnyTenant<AdminUser>(u)).ToArray();
            users.Length.ShouldBeGreaterThan(0);
        }
    }
}