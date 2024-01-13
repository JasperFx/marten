using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class query_through_mixed_population_Tests: end_to_end_document_hierarchy_usage_Tests
{
    public query_through_mixed_population_Tests()
    {
        loadData();
    }

    [Fact]
    public void clean_by_subclass_only_deletes_the_one_subclass()
    {
        TheStore.Advanced.Clean.DeleteDocumentsByType(typeof(AdminUser));

        using var session = TheStore.IdentitySession();
        session.Query<User>().Any().ShouldBeTrue();
        session.Query<SuperUser>().Any().ShouldBeTrue();

        session.Query<AdminUser>().Any().ShouldBeFalse();
    }


    [Fact]
    public void identity_map_usage_from_select()
    {
        using var session = identitySessionWithData();
        var users = session.Query<User>().OrderBy(x => x.FirstName).ToArray();
        users[0].ShouldBeTheSameAs(admin1);
        users[1].ShouldBeTheSameAs(super1);
        users[5].ShouldBeTheSameAs(user2);
    }

    [Fact]
    public void load_by_id_keys_from_base_class_clean()
    {
        using var session = TheStore.QuerySession();
        session.LoadMany<AdminUser>(admin1.Id, admin2.Id)
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(admin1.Id, admin2.Id);
    }

    [Fact]
    public void load_by_id_keys_from_base_class_resolved_from_identity_map()
    {
        using var session = identitySessionWithData();
        session.LoadMany<AdminUser>(admin1.Id, admin2.Id)
            .ShouldHaveTheSameElementsAs(admin1, admin2);
    }

    [Fact]
    public async Task load_by_id_keys_from_base_class_resolved_from_identity_map_async()
    {
        await using var session = identitySessionWithData();
        var users = await session.LoadManyAsync<AdminUser>(admin1.Id, admin2.Id);
        users.ShouldHaveTheSameElementsAs(admin1, admin2);
    }

    [Fact]
    public void load_by_id_with_mixed_results_fresh()
    {
        using var session = TheStore.QuerySession();
        session.LoadMany<User>(admin1.Id, super1.Id, user1.Id)
            .ToArray()
            .OrderBy(x => x.FirstName)
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
    }

    [Fact]
    public async Task load_by_id_with_mixed_results_fresh_async()
    {
        await using var session = TheStore.QuerySession();
        var users = await session.LoadManyAsync<User>(admin1.Id, super1.Id, user1.Id);

        users.OrderBy(x => x.FirstName)
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
    }

    [Fact]
    public void load_by_id_with_mixed_results_from_identity_map()
    {
        using var session = identitySessionWithData();
        session.LoadMany<User>(admin1.Id, super1.Id, user1.Id)
            .ToArray().ShouldHaveTheSameElementsAs(admin1, super1, user1);
    }

    [Fact]
    public async Task load_by_id_with_mixed_results_from_identity_map_async()
    {
        await using var session = identitySessionWithData();
        var users = await session.LoadManyAsync<User>(admin1.Id, super1.Id, user1.Id);
        users.OrderBy(x => x.FirstName).ShouldHaveTheSameElementsAs(admin1, super1, user1);
    }

    [Fact]
    public void query_against_all_with_no_where()
    {
        using var session = TheStore.IdentitySession();
        var users = session.Query<User>().OrderBy(x => x.FirstName).ToArray();
        users
            .Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, admin2.Id, user1.Id, super2.Id, user2.Id);

        users.Select(x => x.GetType())
            .ShouldHaveTheSameElementsAs(typeof(AdminUser), typeof(SuperUser), typeof(AdminUser), typeof(User),
                typeof(SuperUser), typeof(User));
    }

    [Fact]
    public void query_against_all_with_where_clause()
    {
        using var session = TheStore.IdentitySession();
        session.Query<User>().OrderBy(x => x.FirstName).Where(x => x.UserName.StartsWith("A"))
            .ToArray().Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
    }

    [Fact]
    public void query_for_only_a_subclass_with_no_where_clause()
    {
        using var session = TheStore.IdentitySession();
        session.Query<AdminUser>().OrderBy(x => x.FirstName).ToArray()
            .Select(x => x.Id).ShouldHaveTheSameElementsAs(admin1.Id, admin2.Id);
    }

    [Fact]
    public void query_for_only_a_subclass_with_where_clause()
    {
        using var session = TheStore.IdentitySession();
        session.Query<AdminUser>().Where(x => x.FirstName == "Eric").Single()
            .Id.ShouldBe(admin2.Id);
    }
}
