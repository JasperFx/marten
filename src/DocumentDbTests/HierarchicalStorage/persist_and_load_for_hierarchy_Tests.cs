using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

public class persist_and_load_for_hierarchy_Tests: end_to_end_document_hierarchy_usage_Tests
{
    [Fact]
    public async Task persist_and_delete_subclass()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1);
        await session.SaveChangesAsync();

        session.Delete(admin1);

        await session.SaveChangesAsync();

        session.Load<User>(admin1.Id).ShouldBeNull();
        session.Load<AdminUser>(admin1.Id).ShouldBeNull();
    }


    [Fact]
    public async Task persist_and_delete_subclass_2()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1);
        await session.SaveChangesAsync();

        session.Delete<AdminUser>(admin1.Id);

        await session.SaveChangesAsync();

        session.Load<User>(admin1.Id).ShouldBeNull();
        session.Load<AdminUser>(admin1.Id).ShouldBeNull();
    }

    [Fact]
    public async Task persist_and_delete_top()
    {
        using var session = theStore.IdentitySession();
        session.Store(user1);
        await session.SaveChangesAsync();

        session.Delete<User>(user1.Id);
        await session.SaveChangesAsync();

        session.Load<User>(user1.Id).ShouldBeNull();
    }

    [Fact]
    public async Task persist_and_delete_top_2()
    {
        using var session = theStore.IdentitySession();
        session.Store(user1);
        await session.SaveChangesAsync();

        session.Delete(user1);
        await session.SaveChangesAsync();

        session.Load<User>(user1.Id).ShouldBeNull();
    }


    [Fact]
    public async Task persist_and_load_subclass()
    {
        using var session = theStore.IdentitySession();
        session.Store(admin1);
        await session.SaveChangesAsync();

        session.Load<User>(admin1.Id).ShouldBeSameAs(admin1);
        session.Load<AdminUser>(admin1.Id).ShouldBeSameAs(admin1);

        using var query = theStore.QuerySession();
        query.Load<AdminUser>(admin1.Id).ShouldNotBeNull().ShouldNotBeSameAs(admin1);
        query.Load<User>(admin1.Id).ShouldNotBeNull().ShouldNotBeSameAs(admin1);
    }

    [Fact]
    public async Task persist_and_load_subclass_async()
    {
        await using var session = theStore.IdentitySession();
        session.Store(admin1);
        await session.SaveChangesAsync();

        (await session.LoadAsync<User>(admin1.Id)).ShouldBeSameAs(admin1);
        (await session.LoadAsync<AdminUser>(admin1.Id)).ShouldBeSameAs(admin1);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<AdminUser>(admin1.Id)).ShouldNotBeNull().ShouldNotBeSameAs(admin1)
            ;
        (await query.LoadAsync<User>(admin1.Id)).ShouldNotBeNull().ShouldNotBeSameAs(admin1);
    }

    [Fact]
    public async Task persist_and_load_top_level()
    {
        using var session = theStore.IdentitySession();
        session.Store(user1);
        await session.SaveChangesAsync();

        session.Load<User>(user1.Id).ShouldBeSameAs(user1);

        using var query = theStore.QuerySession();
        query.Load<User>(user1.Id).ShouldNotBeNull().ShouldNotBeSameAs(user1);
    }
}
