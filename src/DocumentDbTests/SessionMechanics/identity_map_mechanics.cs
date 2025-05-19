﻿using System;
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
    public async Task when_loading_then_the_document_should_be_returned(DocumentTracking tracking)
    {
        var user = new User { FirstName = "Tim", LastName = "Cools" };
        var session = OpenSession(tracking);
        session.Store(user);
        await session.SaveChangesAsync();

        using var identitySession = theStore.IdentitySession();
        var first = await identitySession.LoadAsync<User>(user.Id);
        var second = await identitySession.LoadAsync<User>(user.Id);

        first.ShouldBeSameAs(second);
    }

    [Theory]
    [SessionTypes]
    public async Task when_loading_by_ids_then_the_same_document_should_be_returned(DocumentTracking tracking)
    {
        var user = new User { FirstName = "Tim", LastName = "Cools" };
        var session = OpenSession(tracking);
        session.Store(user);
        await session.SaveChangesAsync();

        using var identitySession = theStore.IdentitySession();
        var first = await identitySession.LoadAsync<User>(user.Id);
        var second = (await identitySession.LoadManyAsync<User>(user.Id))
            .SingleOrDefault();

        first.ShouldBeSameAs(second);
    }

    [Fact]
    public async Task when_querying_and_modifying_multiple_documents_should_track_and_persist()
    {
        var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
        var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
        var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

        using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3);

        await session.SaveChangesAsync();

        using (var session2 = theStore.DirtyTrackedSession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

            foreach (var user in users)
            {
                user.LastName += " - updated";
            }

            await session2.SaveChangesAsync();
        }

        using (var session2 = theStore.IdentitySession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").OrderBy(x => x.LastName).ToList();

            users.Select(x => x.LastName)
                .ShouldHaveTheSameElementsAs("Worthy 1 - updated", "Worthy 2 - updated", "Worthy 3 - updated");
        }
    }

    [Fact]
    public async Task when_querying_and_modifying_multiple_documents_should_track_and_persist_dirty()
    {
        var user1 = new User { FirstName = "James", LastName = "Worthy 1" };
        var user2 = new User { FirstName = "James", LastName = "Worthy 2" };
        var user3 = new User { FirstName = "James", LastName = "Worthy 3" };

        var session = theStore.DirtyTrackedSession();
        session.Store(user1);
        session.Store(user2);
        session.Store(user3);

        await session.SaveChangesAsync();

        using (var session2 = theStore.DirtyTrackedSession())
        {
            var users = session2.Query<User>().Where(x => x.FirstName == "James").ToList();

            foreach (var user in users)
            {
                user.LastName += " - updated";
            }

            await session2.SaveChangesAsync();
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
    public async Task then_a_document_can_be_added_with_then_specified_id(DocumentTracking tracking)
    {
        var id = Guid.NewGuid();

        var session = OpenSession(tracking);
        var notFound = await session.LoadAsync<User>(id);

        var replacement = new User { Id = id, FirstName = "Tim", LastName = "Cools" };

        session.Store(replacement);
    }


    [Theory]
    [InlineData(DocumentTracking.DirtyTracking)]
    [InlineData(DocumentTracking.IdentityOnly)]
    public async Task finding_documents_in_map_but_not_yet_persisted(DocumentTracking tracking)
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        var session = OpenSession(tracking);
        session.Store(user1);

        var fromSession = await session.LoadAsync<User>(user1.Id);

        fromSession.ShouldBeSameAs(user1);
    }

    [Theory]
    [InlineData(DocumentTracking.DirtyTracking)]
    [InlineData(DocumentTracking.IdentityOnly)]
    public async Task finding_documents_in_map_but_not_yet_persisted_2(DocumentTracking tracking)
    {
        var user1 = new User { FirstName = "Tim", LastName = "Cools" };

        var session = OpenSession(tracking);
        session.Store(user1);
        session.Store(user1);

        var fromSession = await session.LoadAsync<User>(user1.Id);

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

        Should.Throw<InvalidOperationException>(() => session.Store(user2))
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

    [Fact]
    public async Task opt_into_identity_map_with_lightweight_sessions()
    {
        var target = Target.Random();

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        await using var lightweight = theStore.LightweightSession();
        lightweight.UseIdentityMapFor<Target>();

        var target1 = await lightweight.LoadAsync<Target>(target.Id);
        var target2 = await lightweight.LoadAsync<Target>(target.Id);
        var target3 = await lightweight.LoadAsync<Target>(target.Id);

        target1.ShouldBeSameAs(target2);
        target1.ShouldBeSameAs(target3);
    }

    public identity_map_mechanics(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
