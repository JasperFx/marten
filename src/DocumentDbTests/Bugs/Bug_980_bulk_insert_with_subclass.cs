using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_980_bulk_insert_with_subclass: BugIntegrationContext
{
    [Fact]
    public async Task can_do_a_bulk_insert_against_the_parent()
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

        await theStore.BulkInsertAsync(users);

        using (var query = theStore.LightweightSession())
        {
            query.Query<AdminUser>().Count().ShouldBe(1);
            query.Query<SuperUser>().Count().ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_do_a_bulk_insert_against_the_child_type()
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

        await theStore.BulkInsertAsync(users);

        using (var query = theStore.LightweightSession())
        {
            query.Query<SuperUser>().Count().ShouldBe(4);
        }
    }

}
