using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class dirty_tracked_sessions: IntegrationContext
{
    public dirty_tracked_sessions(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public void store_and_update_a_document()
    {
        var user = new User { FirstName = "James", LastName = "Worthy" };

        using var session = theStore.DirtyTrackedSession();
        session.Store(user);
        session.SaveChanges();

        using (var session2 = theStore.DirtyTrackedSession())
        {
            session2.ShouldNotBeSameAs(session);

            var user2 = session2.Load<User>(user.Id);
            user2.FirstName = "Jens";
            user2.LastName = "Pettersson";

            session2.SaveChanges();
        }

        using (var session3 = theStore.LightweightSession())
        {
            var user3 = session3.Load<User>(user.Id);
            user3.FirstName.ShouldBe("Jens");
            user3.LastName.ShouldBe("Pettersson");
        }
    }

    [Fact]
    public void store_and_update_a_document_in_same_session()
    {
        var user = new User { FirstName = "James", LastName = "Worthy" };

        using var session = theStore.DirtyTrackedSession();
        session.Store(user);
        session.SaveChanges();

        user.FirstName = "Jens";
        user.LastName = "Pettersson";
        session.SaveChanges();

        using var session3 = theStore.LightweightSession();
        var user3 = session3.Load<User>(user.Id);
        user3.FirstName.ShouldBe("Jens");
        user3.LastName.ShouldBe("Pettersson");
    }


    [Fact]
    public void store_reload_and_update_a_document_in_same_dirty_tracked_session()
    {
        var user = new User { FirstName = "James", LastName = "Worthy" };

        using var session = theStore.DirtyTrackedSession();
        session.Store(user);
        session.SaveChanges();

        var user2 = session.Load<User>(user.Id);
        user2.FirstName = "Jens";
        user2.LastName = "Pettersson";
        session.SaveChanges();

        using var query = theStore.QuerySession();
        var user3 = query.Load<User>(user.Id);
        user3.FirstName.ShouldBe("Jens");
        user3.LastName.ShouldBe("Pettersson");
    }
}
