using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class identity_map_mechanics: IntegrationContext
{
    [Theory]
    [SessionTypes]
    public void when_loading_then_the_document_should_be_returned(DocumentTracking tracking)
    {
        var user = new User { FirstName = "Tim", LastName = "Cools" };
        var session = OpenSession(tracking);
        session.Store(user);
        session.SaveChanges();

        using var identitySession = theStore.IdentitySession();
        var first = identitySession.Load<User>(user.Id);
        var second = identitySession.Load<User>(user.Id);

        first.ShouldBeSameAs(second);
    }

    [Theory]
    [SessionTypes]
    public void when_loading_by_ids_then_the_same_document_should_be_returned(DocumentTracking tracking)
    {
        var user = new User { FirstName = "Tim", LastName = "Cools" };
        var session = OpenSession(tracking);
        session.Store(user);
        session.SaveChanges();

        using var identitySession = theStore.IdentitySession();
        var first = identitySession.Load<User>(user.Id);
        var second = identitySession.LoadMany<User>(user.Id)
            .SingleOrDefault();

        first.ShouldBeSameAs(second);
    }

    [Fact]
    public void when_querying_and_modifying_multiple_documents_should_track_and_persist()
    {
        var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
        var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
        var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);

        session.SaveChanges();

        using (var session2 = theStore.DirtyTrackedSession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

            foreach (var user in users)
            {
                user.LastName += " - updated";
            }

            session2.SaveChanges();
        }

        using (var session2 = theStore.IdentitySession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").OrderBy(x => x.LastName).ToList();

            users.Select(x => x.LastName)
                .ShouldHaveTheSameElementsAs("Worthy 1 - updated", "Worthy 2 - updated", "Worthy 3 - updated");
        }
    }

    [Fact]
    public void when_querying_and_modifying_multiple_documents_should_track_and_persist_dirty()
    {
        var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
        var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
        var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

        var session = theStore.DirtyTrackedSession();
        session.Store(user1);
        session.Store(user2);
        session.Store(user3);

        session.SaveChanges();

        using (var session2 = theStore.DirtyTrackedSession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

            foreach (var user in users)
            {
                user.LastName += " - updated";
            }

            session2.SaveChanges();
        }

        using (var session2 = theStore.IdentitySession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").OrderBy(x => x.LastName).ToList();

            users.Select(x => x.LastName)
                .ShouldHaveTheSameElementsAs("Worthy 1 - updated", "Worthy 2 - updated", "Worthy 3 - updated");
        }
    }

    [Theory]
    [SessionTypes]
    public void then_a_document_can_be_added_with_then_specified_id(DocumentTracking tracking)
    {
        var id = Guid.NewGuid();

        var session = OpenSession(tracking);
        var notFound = session.Load<User>(id);

        var replacement = new User { Id = id, FirstName = "Tim", LastName = "Cools" };

        session.Store(replacement);
    }


    [Theory]
    [InlineData(DocumentTracking.DirtyTracking)]
    [InlineData(DocumentTracking.IdentityOnly)]
    public void finding_documents_in_map_but_not_yet_persisted(DocumentTracking tracking)
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        var session = OpenSession(tracking);
        session.Store(user1);

        var fromSession = session.Load<User>(user1.Id);

        fromSession.ShouldBeSameAs(user1);
    }

    [Theory]
    [InlineData(DocumentTracking.DirtyTracking)]
    [InlineData(DocumentTracking.IdentityOnly)]
    public void finding_documents_in_map_but_not_yet_persisted_2(DocumentTracking tracking)
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        var session = OpenSession(tracking);
        session.Store(user1);
        session.Store(user1);

        var fromSession = session.Load<User>(user1.Id);

        fromSession.ShouldBeSameAs(user1);
    }

    [Theory]
    [InlineData(DocumentTracking.DirtyTracking)]
    [InlineData(DocumentTracking.IdentityOnly)]
    public void given_document_with_same_id_is_already_added_then_exception_should_occur(DocumentTracking tracking)
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };
        var user2 = new User { FirstName = "Tim2", LastName = "Cools2", Id = user1.Id };

        var session = OpenSession(tracking);
        session.Store(user1);

        Exception<InvalidOperationException>.ShouldBeThrownBy(() => session.Store(user2))
            .Message.ShouldBe("Document 'Marten.Testing.Documents.User' with same Id already added to the session.");
    }

    [Fact]
    public async Task given_record_with_same_id_already_added_then_map_should_be_updated()
    {
        var initialState = new FriendCount(Guid.NewGuid(), 1);

        await using var store = theStore.IdentitySession();

        store.Store(initialState);

        var updated = initialState with { Number = 3 };

        store.Store(updated);

        var entity = await store.LoadAsync<FriendCount>(updated.Id);

        Assert.Equal(updated, entity);

    }

    public identity_map_mechanics(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
