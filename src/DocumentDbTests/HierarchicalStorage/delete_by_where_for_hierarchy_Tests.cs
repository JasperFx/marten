using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class delete_by_where_for_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public void can_delete_all_subclass()
    {
        loadData();

        theSession.DeleteWhere<SuperUser>(x => true);
        theSession.SaveChanges();

        theSession.Query<SuperUser>().Count().ShouldBe(0);
        theSession.Query<AdminUser>().Count().ShouldBe(2);
        theSession.Query<User>().Count().ShouldBe(4);
    }

    [Fact]
    public void can_delete_by_subclass()
    {
        loadData();

        theSession.DeleteWhere<SuperUser>(x => x.FirstName.StartsWith("D"));
        theSession.SaveChanges();

        theSession.Query<SuperUser>().Count().ShouldBe(1);
        theSession.Query<AdminUser>().Count().ShouldBe(2);
        theSession.Query<User>().Count().ShouldBe(5);
    }

    [Fact]
    public void can_delete_by_the_hierarchy()
    {
        loadData();

        theSession.DeleteWhere<User>(x => x.FirstName.StartsWith("D"));
        theSession.SaveChanges();

        // Should delete one SuperUser and one AdminUser
        theSession.Query<SuperUser>().Count().ShouldBe(1);
        theSession.Query<AdminUser>().Count().ShouldBe(1);
        theSession.Query<User>().Count().ShouldBe(4);
    }
}
