using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using User = Marten.Testing.Examples.User;

namespace Marten.Testing.MultiTenancy
{
    public class cannot_update_documents_across_tenants : IntegrationContext
    {
        [Fact]
        public void will_not_cross_the_streams()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            var user = new User {UserName = "Me"};
            user.Id = Guid.NewGuid();

            using (var red = theStore.OpenSession("Red"))
            {
                red.Store(user);
                red.SaveChanges();
            }

            using (var green = theStore.OpenSession("Green"))
            {
                var greenUser = new User
                {
                    UserName = "You",
                    Id = user.Id
                };

                // Nothing should happen here
                green.Store(greenUser);
                green.SaveChanges();
            }

            // Still got the original data
            using (var query = theStore.QuerySession("Red"))
            {
                query.Load<User>(user.Id).UserName.ShouldBe("Me");
            }
        }

        [Fact]
        public void patching_respects_tenancy_too()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            var user = new User { UserName = "Me", FirstName = "Jeremy", LastName = "Miller"};
            user.Id = Guid.NewGuid();

            using (var red = theStore.OpenSession("Red"))
            {
                red.Store(user);
                red.SaveChanges();
            }

            using (var green = theStore.OpenSession("Green"))
            {
                green.Patch<User>(user.Id).Set(x => x.FirstName, "John");
                green.SaveChanges();
            }

            using (var red = theStore.QuerySession("Red"))
            {
                var final = red.Load<User>(user.Id);
                final.FirstName.ShouldBe("Jeremy");
            }
        }

        [Fact]
        public void patching_respects_tenancy_too_2()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            var user = new User { UserName = "Me", FirstName = "Jeremy", LastName = "Miller" };
            user.Id = Guid.NewGuid();

            using (var red = theStore.OpenSession("Red"))
            {
                red.Store(user);
                red.SaveChanges();
            }

            using (var green = theStore.OpenSession("Green"))
            {
                green.Patch<User>(x => x.UserName == "Me").Set(x => x.FirstName, "John");
                green.SaveChanges();
            }

            using (var red = theStore.QuerySession("Red"))
            {
                var final = red.Load<User>(user.Id);
                final.FirstName.ShouldBe("Jeremy");
            }
        }

        [Fact]
        public void bulk_insert_respects_tenancy()
        {
            var reds = Target.GenerateRandomData(20).ToArray();
            var greens = Target.GenerateRandomData(15).ToArray();

            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            theStore.BulkInsert("Red", reds);
            theStore.BulkInsert("Green", greens);

            Guid[] actualReds = null;
            Guid[] actualGreens = null;

            using (var query = theStore.QuerySession("Red"))
            {
                actualReds = query.Query<Target>().Select(x => x.Id).ToArray();
            }

            using (var query = theStore.QuerySession("Green"))
            {
                actualGreens = query.Query<Target>().Select(x => x.Id).ToArray();
            }

            actualGreens.Intersect(actualReds).Any().ShouldBeFalse();
        }

        public cannot_update_documents_across_tenants(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
