using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_1884_multi_tenancy_and_Any_query: BugIntegrationContext
{
    public class User
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string[] Roles { get; set; }
    }

    public Bug_1884_multi_tenancy_and_Any_query(ITestOutputHelper output)
    {
        StoreOptions(x =>
        {
            x.Policies.AllDocumentsAreMultiTenanted();
            x.Logger(new TestOutputMartenLogger(output));
        });
    }

    [Fact]
    public async Task any_filter_honors_tenancy()
    {
        var store = theStore;

        // Write some User documents to tenant "tenant1"
        await using (var session = store.LightweightSession("tenant1"))
        {
            session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
            session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
            await session.SaveChangesAsync();
        }

        // Write some User documents to tenant "tenant2"
        await using (var session = store.LightweightSession("tenant2"))
        {
            session.Store(new User { Id = "u1", UserName = "Frank", Roles = new string[0] });
            session.Store(new User { Id = "u2", UserName = "Jill", Roles = new[] { "admin", "user" } });
            await session.SaveChangesAsync();
        }

        // When you query for data from the "tenant1" tenant,
        // you only get data for that tenant
        var validRoles = new[] { "admin", "user" };

        await using (var query = store.QuerySession("tenant1"))
        {
            query.Query<User>()
                .Where(x => x.Roles.Any(_ => validRoles.Contains(_)) && x.AnyTenant())
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Bill", "Jill");
        }
    }

    [Fact]
    public async Task will_isolate_tenants_when_using_any_and_tenants_use_unique_ids()
    {
        #region sample_tenancy-scoping-session-write

        // Write some User documents to tenant "tenant1"
        using (var session = theStore.LightweightSession("tenant1"))
        {
            session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
            session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
            await session.SaveChangesAsync();
        }

        #endregion sample_tenancy-scoping-session-write

        // Write some User documents to tenant "tenant2"
        using (var session = theStore.LightweightSession("tenant2"))
        {
            session.Store(new User { Id = "u3", UserName = "Frank", Roles = new string[0] });
            session.Store(new User { Id = "u4", UserName = "Jill", Roles = new[] { "admin", "user" } });
            await session.SaveChangesAsync();
        }

        // When you query for data from the "tenant1" tenant,
        // you only get data for that tenant

        var validRoles = new[] { "admin", "user" };

        using (var query = theStore.QuerySession("tenant1"))
        {
            query.Query<User>()
                .Where(x => x.Roles.Any(_ => validRoles.Contains(_)))
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Bill");
        }

        using (var query = theStore.QuerySession("tenant2"))
        {
            query.Query<User>()
                .Where(x => x.Roles.Any(_ => validRoles.Contains(_)))
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Jill");
        }
    }

    [Fact]
    public async Task can_query_with_AnyTenant()
    {
        #region sample_tenancy-scoping-session-write

        // Write some User documents to tenant "tenant1"
        using (var session = theStore.LightweightSession("tenant1"))
        {
            session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
            session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
            await session.SaveChangesAsync();
        }

        #endregion sample_tenancy-scoping-session-write

        // Write some User documents to tenant "tenant2"
        using (var session = theStore.LightweightSession("tenant2"))
        {
            session.Store(new User { Id = "u3", UserName = "Frank", Roles = new string[0] });
            session.Store(new User { Id = "u4", UserName = "Jill", Roles = new[] { "admin", "user" } });
            await session.SaveChangesAsync();
        }

        // When you query for data from the "tenant1" tenant,
        // you only get data for that tenant

        var validRoles = new[] { "admin", "user" };

        using (var query = theStore.QuerySession("tenant1"))
        {
            query.Query<User>()
                .Where(x => x.Roles.Any(_ => validRoles.Contains(_)) && x.AnyTenant())
                .OrderBy(x => x.UserName)
                .Select(x => x.UserName)
                .ShouldHaveTheSameElementsAs("Bill", "Jill");
        }
    }
}
