using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class Bug_1484_store_overloads_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public async Task persist_and_count_single_entity()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1);
        await session.SaveChangesAsync();

        session.Query<AdminUser>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task persist_mutliple_entites_as_params_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1, admin2);
        await session.SaveChangesAsync();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task persist_mutliple_entites_as_array_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(new[] { admin1, admin2 });
        await session.SaveChangesAsync();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task persist_mutliple_entites_as_enumerable_and_count()
    {
        using var session = theStore.IdentitySession();
        session.Store(new[] { admin1, admin2 }.AsEnumerable());
        await session.SaveChangesAsync();

        session.Query<AdminUser>().Count().ShouldBe(2);
    }
}
