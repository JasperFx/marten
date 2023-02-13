using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class Bug_1484_store_overloads_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public void persist_and_count_single_entity()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1);
        session.SaveChanges();

        session.Query<AdminUser>().Count().ShouldBe(1);
    }

    [Fact]
    public void persist_mutliple_entites_as_params_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1, admin2);
        session.SaveChanges();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }

    [Fact]
    public void persist_mutliple_entites_as_array_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(new[] { admin1, admin2 });
        session.SaveChanges();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }

    [Fact]
    public void persist_mutliple_entites_as_enumerable_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(new[] { admin1, admin2 }.AsEnumerable());
        session.SaveChanges();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }
}
