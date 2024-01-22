using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.HierarchicalStorage;

public class delete_by_where_for_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public void can_delete_all_subclass()
    {
        loadData();

        TheSession.DeleteWhere<SuperUser>(x => true);
        TheSession.SaveChanges();

        TheSession.Query<SuperUser>().Count().ShouldBe(0);
        TheSession.Query<AdminUser>().Count().ShouldBe(2);
        TheSession.Query<User>().Count().ShouldBe(4);
    }

    [Fact]
    public void can_delete_by_subclass()
    {
        loadData();

        TheSession.DeleteWhere<SuperUser>(x => x.FirstName.StartsWith("D"));
        TheSession.SaveChanges();

        TheSession.Query<SuperUser>().Count().ShouldBe(1);
        TheSession.Query<AdminUser>().Count().ShouldBe(2);
        TheSession.Query<User>().Count().ShouldBe(5);
    }

    [Fact]
    public void can_delete_by_the_hierarchy()
    {
        loadData();

        TheSession.DeleteWhere<User>(x => x.FirstName.StartsWith("D"));
        TheSession.SaveChanges();

        // Should delete one SuperUser and one AdminUser
        TheSession.Query<SuperUser>().Count().ShouldBe(1);
        TheSession.Query<AdminUser>().Count().ShouldBe(1);
        TheSession.Query<User>().Count().ShouldBe(4);
    }
}
