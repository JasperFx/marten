using System.Linq;
using Shouldly;
using Xunit;
using Marten.Testing.Documents;

namespace Marten.Testing.Bugs
{
    public class Bug_980_bulk_insert_with_subclass : IntegratedFixture
    {
        [Fact]
        public void can_do_a_bulk_insert_against_the_parent()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var users = new User[]
            {
                new AdminUser { UserName = "foo" },
                new SuperUser { UserName = "bar" },
                new SuperUser { UserName = "myergen" }
            };

            theStore.BulkInsert(users);

            using (var query = theStore.LightweightSession())
            {
                query.Query<AdminUser>().Count().ShouldBe(1);
                query.Query<SuperUser>().Count().ShouldBe(2);
            }
        }

        [Fact]
        public void can_do_a_bulk_insert_against_the_child_type()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>()
                    .AddSubClass<AdminUser>()
                    .AddSubClass<SuperUser>();
            });

            var users = new SuperUser[]
            {
                new SuperUser { UserName = "bar" },
                new SuperUser { UserName = "myergen" },
                new SuperUser { UserName = "else" },
                new SuperUser { UserName = "more" }
            };

            theStore.BulkInsert(users);

            using (var query = theStore.LightweightSession())
            {
                query.Query<SuperUser>().Count().ShouldBe(4);
            }
        }
    }
}
