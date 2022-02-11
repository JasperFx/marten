using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Bugs
{
    public class Bug_1884_multi_tenancy_and_Any_query : BugIntegrationContext
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
            using (var session = store.OpenSession("tenant1"))
            {
                session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
                session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
                await session.SaveChangesAsync();
            }

            // Write some User documents to tenant "tenant2"
            using (var session = store.OpenSession("tenant2"))
            {
                session.Store(new User { Id = "u1", UserName = "Frank", Roles = new string[0] });
                session.Store(new User { Id = "u2", UserName = "Jill", Roles = new []{"admin", "user"} });
                await session.SaveChangesAsync();
            }


            // When you query for data from the "tenant1" tenant,
            // you only get data for that tenant

            var validRoles = new[] {"admin", "user"};

            using (var query = store.OpenSession("tenant1"))
            {
                query.Query<User>()
                    .Where(x => x.Roles.Any(_ => validRoles.Contains(_)) && x.AnyTenant())
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ShouldHaveTheSameElementsAs("Bill", "Jill");
            }

        }


        [Fact]
        public void will_isolate_tenants_when_using_any_and_tenants_use_unique_ids()
        {
            #region sample_tenancy-scoping-session-write
            // Write some User documents to tenant "tenant1"
            using (var session = theStore.OpenSession("tenant1"))
            {
                session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
                session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
                session.SaveChanges();
            }
            #endregion sample_tenancy-scoping-session-write

            // Write some User documents to tenant "tenant2"
            using (var session = theStore.OpenSession("tenant2"))
            {
                session.Store(new User { Id = "u3", UserName = "Frank", Roles = new string[0] });
                session.Store(new User { Id = "u4", UserName = "Jill", Roles = new[] { "admin", "user" } });
                session.SaveChanges();
            }

            // When you query for data from the "tenant1" tenant,
            // you only get data for that tenant

            var validRoles = new[] { "admin", "user" };

            using (var query = theStore.OpenSession("tenant1"))
            {
                query.Query<User>()
                    .Where(x => x.Roles.Any(_ => validRoles.Contains(_)))
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ShouldHaveTheSameElementsAs("Bill");
            }

            using (var query = theStore.OpenSession("tenant2"))
            {
                query.Query<User>()
                    .Where(x => x.Roles.Any(_ => validRoles.Contains(_)))
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ShouldHaveTheSameElementsAs("Jill");
            }
        }

        [Fact]
        public void can_query_with_AnyTenant()
        {
            #region sample_tenancy-scoping-session-write
            // Write some User documents to tenant "tenant1"
            using (var session = theStore.OpenSession("tenant1"))
            {
                session.Store(new User { Id = "u1", UserName = "Bill", Roles = new[] { "admin" } });
                session.Store(new User { Id = "u2", UserName = "Lindsey", Roles = new string[0] });
                session.SaveChanges();
            }
            #endregion sample_tenancy-scoping-session-write

            // Write some User documents to tenant "tenant2"
            using (var session = theStore.OpenSession("tenant2"))
            {
                session.Store(new User { Id = "u3", UserName = "Frank", Roles = new string[0] });
                session.Store(new User { Id = "u4", UserName = "Jill", Roles = new[] { "admin", "user" } });
                session.SaveChanges();
            }

            // When you query for data from the "tenant1" tenant,
            // you only get data for that tenant

            var validRoles = new[] { "admin", "user" };

            using (var query = theStore.OpenSession("tenant1"))
            {
                query.Query<User>()
                    .Where(x => x.Roles.Any(_ => validRoles.Contains(_)) && x.AnyTenant())
                    .OrderBy(x => x.UserName)
                    .Select(x => x.UserName)
                    .ShouldHaveTheSameElementsAs("Bill", "Jill");
            }

        }
    }
}
