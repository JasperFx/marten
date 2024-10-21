using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.HierarchicalStorage;

public class delete_by_where_for_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public async Task can_delete_all_subclass()
    {
        await loadData();

        theSession.DeleteWhere<SuperUser>(x => true);
        await theSession.SaveChangesAsync();

        theSession.Query<SuperUser>().Count().ShouldBe(0);
        theSession.Query<AdminUser>().Count().ShouldBe(2);
        theSession.Query<User>().Count().ShouldBe(4);
    }

    [Fact]
    public async Task can_delete_by_subclass()
    {
        await loadData();

        theSession.DeleteWhere<SuperUser>(x => x.FirstName.StartsWith("D"));
        await theSession.SaveChangesAsync();

        theSession.Query<SuperUser>().Count().ShouldBe(1);
        theSession.Query<AdminUser>().Count().ShouldBe(2);
        theSession.Query<User>().Count().ShouldBe(5);
    }

    [Fact]
    public async Task can_delete_by_the_hierarchy()
    {
        await loadData();

        theSession.DeleteWhere<User>(x => x.FirstName.StartsWith("D"));
        await theSession.SaveChangesAsync();

        // Should delete one SuperUser and one AdminUser
        theSession.Query<SuperUser>().Count().ShouldBe(1);
        theSession.Query<AdminUser>().Count().ShouldBe(1);
        theSession.Query<User>().Count().ShouldBe(4);
    }
}
