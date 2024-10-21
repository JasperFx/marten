using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Writing;

public class storing_documents: IntegrationContext
{
    public storing_documents(DefaultStoreFixture fixture): base(fixture)
    {
    }


    [Theory]
    [InlineData(DocumentTracking.IdentityOnly)]
    [InlineData(DocumentTracking.None)]
    public async Task store_a_document(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        var user = new User { FirstName = "James", LastName = "Worthy" };

        session.Store(user);
        await session.SaveChangesAsync();

        using var session3 = theStore.LightweightSession();
        var user3 = session3.Load<User>(user.Id);
        user3.FirstName.ShouldBe("James");
        user3.LastName.ShouldBe("Worthy");
    }

    [Theory]
    [InlineData(DocumentTracking.IdentityOnly)]
    [InlineData(DocumentTracking.None)]
    public async Task store_and_update_a_document_then_document_should_not_be_updated(DocumentTracking tracking)
    {
        var user = new User { FirstName = "James", LastName = "Worthy" };

        using var session = OpenSession(tracking);
        session.Store(user);
        await session.SaveChangesAsync();

        using (var session2 = theStore.LightweightSession())
        {
            session2.ShouldNotBeSameAs(session);

            var user2 = session2.Load<User>(user.Id);
            user2.FirstName = "Jens";
            user2.LastName = "Pettersson";

            await session2.SaveChangesAsync();
        }

        using (var session3 = theStore.LightweightSession())
        {
            var user3 = session3.Load<User>(user.Id);
            user3.FirstName.ShouldBe("James");
            user3.LastName.ShouldBe("Worthy");
        }
    }

    [Theory]
    [InlineData(DocumentTracking.IdentityOnly)]
    [InlineData(DocumentTracking.None)]
    public async Task store_and_update_a_document_in_same_session_then_document_should_not_be_updated(
        DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        var user = new User { FirstName = "James", LastName = "Worthy" };

        session.Store(user);
        await session.SaveChangesAsync();

        user.FirstName = "Jens";
        user.LastName = "Pettersson";
        await session.SaveChangesAsync();

        using var session3 = theStore.QuerySession();
        var user3 = session3.Load<User>(user.Id);
        user3.FirstName.ShouldBe("James");
        user3.LastName.ShouldBe("Worthy");
    }

    [Theory]
    [InlineData(DocumentTracking.IdentityOnly)]
    [InlineData(DocumentTracking.None)]
    public async Task store_reload_and_update_a_document_in_same_session_then_document_should_not_be_updated(
        DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        var user = new User { FirstName = "James", LastName = "Worthy" };

        session.Store(user);
        await session.SaveChangesAsync();

        var user2 = session.Load<User>(user.Id);
        user2.FirstName = "Jens";
        user2.LastName = "Pettersson";
        await session.SaveChangesAsync();

        using var querySession = theStore.QuerySession();
        var user3 = querySession.Load<User>(user.Id);
        user3.FirstName.ShouldBe("James");
        user3.LastName.ShouldBe("Worthy");
    }

    [Fact]
    public void store_document_inherited_from_document_with_id_from_another_assembly()
    {
        using var session = theStore.IdentitySession();
        var user = new UserFromBaseDocument();
        session.Store(user);
        session.Load<UserFromBaseDocument>(user.Id).ShouldBeTheSameAs(user);
    }

    [Theory]
    [SessionTypes]
    public async Task persist_a_single_document(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);
        var user = new User { FirstName = "Magic", LastName = "Johnson" };

        session.Store(user);

        await session.SaveChangesAsync();

        using var conn = theStore.Tenancy.Default.Database.CreateConnection();
        conn.Open();

        var reader = conn.CreateCommand($"select data from mt_doc_user where id = '{user.Id}'").ExecuteReader();
        reader.Read();

        var loadedUser = new JsonNetSerializer().FromJson<User>(reader, 0);

        user.ShouldNotBeSameAs(loadedUser);
        loadedUser.FirstName.ShouldBe(user.FirstName);
        loadedUser.LastName.ShouldBe(user.LastName);
    }

    [Theory]
    [SessionTypes]
    public async Task persist_and_reload_a_document(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        var user = new User { FirstName = "James", LastName = "Worthy" };

        // session is Marten's IDocumentSession service
        session.Store(user);
        await session.SaveChangesAsync();

        using var session2 = theStore.LightweightSession();
        session2.ShouldNotBeSameAs(session);

        var user2 = session2.Load<User>(user.Id);

        user.ShouldNotBeSameAs(user2);
        user2.FirstName.ShouldBe(user.FirstName);
        user2.LastName.ShouldBe(user.LastName);
    }

    [Theory]
    [SessionTypes]
    public async Task persist_and_reload_a_document_async(DocumentTracking tracking)
    {
        await using var session = OpenSession(tracking);

        var user = new User { FirstName = "James", LastName = "Worthy" };

        // session is Marten's IDocumentSession service
        session.Store(user);
        await session.SaveChangesAsync();

        await using var session2 = theStore.LightweightSession();
        session2.ShouldNotBeSameAs(session);

        var user2 = await session2.LoadAsync<User>(user.Id);

        user.ShouldNotBeSameAs(user2);
        user2.FirstName.ShouldBe(user.FirstName);
        user2.LastName.ShouldBe(user.LastName);
    }

    [Theory]
    [SessionTypes]
    public void try_to_load_a_document_that_does_not_exist(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);
        session.Load<User>(Guid.NewGuid()).ShouldBeNull();
    }

    [Theory]
    [SessionTypes]
    public async Task load_by_id_array(DocumentTracking tracking)
    {
        using var session = OpenSession(tracking);

        var user1 = new User { FirstName = "Magic", LastName = "Johnson" };
        var user2 = new User { FirstName = "James", LastName = "Worthy" };
        var user3 = new User { FirstName = "Michael", LastName = "Cooper" };
        var user4 = new User { FirstName = "Mychal", LastName = "Thompson" };
        var user5 = new User { FirstName = "Kurt", LastName = "Rambis" };

        session.Store(user1);
        session.Store(user2);
        session.Store(user3);
        session.Store(user4);
        session.Store(user5);
        await session.SaveChangesAsync();

        using var querySession = theStore.QuerySession();
        var users = querySession.LoadMany<User>(user2.Id, user3.Id, user4.Id);
        users.Count().ShouldBe(3);
    }

    [Theory]
    [SessionTypes]
    public async Task load_by_id_array_async(DocumentTracking tracking)
    {
        await using var session = OpenSession(tracking);

        #region sample_saving-changes-async

        var user1 = new User { FirstName = "Magic", LastName = "Johnson" };
        var user2 = new User { FirstName = "James", LastName = "Worthy" };
        var user3 = new User { FirstName = "Michael", LastName = "Cooper" };
        var user4 = new User { FirstName = "Mychal", LastName = "Thompson" };
        var user5 = new User { FirstName = "Kurt", LastName = "Rambis" };

        session.Store(user1);
        session.Store(user2);
        session.Store(user3);
        session.Store(user4);
        session.Store(user5);

        await session.SaveChangesAsync();

        #endregion

        var store = theStore;

        #region sample_load_by_id_array_async

        await using var querySession = store.QuerySession();
        var users = await querySession.LoadManyAsync<User>(user2.Id, user3.Id, user4.Id);
        users.Count().ShouldBe(3);

        #endregion
    }
}
