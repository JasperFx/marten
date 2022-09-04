using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class Bug_1484_store_overloads_Tests: end_to_end_document_hierarchy_usage_Tests
{
    public Bug_1484_store_overloads_Tests()
    {
        DocumentTracking = DocumentTracking.IdentityOnly;
    }

    [Fact]
    public void persist_and_count_single_entity()
    {
        theSession.Store(admin1);
        theSession.SaveChanges();

        theSession.Query<AdminUser>().Count().ShouldBe(1);
    }

    [Fact]
    public void persist_mutliple_entites_as_params_and_count()
    {
        theSession.Store(admin1, admin2);
        theSession.SaveChanges();

        theSession.Query<AdminUser>().Count().ShouldBe(2);
    }

    [Fact]
    public void persist_mutliple_entites_as_array_and_count()
    {
        theSession.Store(new[] { admin1, admin2 });
        theSession.SaveChanges();

        theSession.Query<AdminUser>().Count().ShouldBe(2);
    }
    [Fact]
    public void persist_mutliple_entites_as_enumerable_and_count()
    {
        theSession.Store(new[] { admin1, admin2 }.AsEnumerable());
        theSession.SaveChanges();

        theSession.Query<AdminUser>().Count().ShouldBe(2);
    }
}