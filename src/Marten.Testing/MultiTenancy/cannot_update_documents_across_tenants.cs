using System;
using Marten.Testing.Examples;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class cannot_update_documents_across_tenants : IntegratedFixture
    {
        [Fact]
        public void will_not_cross_the_streams()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString)
                    .MultiTenanted();
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
    }
}