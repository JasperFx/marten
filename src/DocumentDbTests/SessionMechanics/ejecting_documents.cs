using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace DocumentDbTests.SessionMechanics;

public class ejecting_documents : IntegrationContext
{
    [Fact]
    public async Task demonstrate_eject()
    {
        #region sample_ejecting_a_document
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);

            // Both documents are in the identity map
            (await session.LoadAsync<Target>(target1.Id)).ShouldBeSameAs(target1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldBeSameAs(target2);

            // Eject the 2nd document
            session.Eject(target2);

            // Now that 2nd document is no longer in the identity map
            (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            // The 2nd document was ejected before the session
            // was saved, so it was never persisted
            (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();
        }
        #endregion
    }

    [Fact]
    public async Task eject_a_document_clears_it_from_the_identity_map_regular()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);

            (await session.LoadAsync<Target>(target1.Id)).ShouldBeSameAs(target1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldBeSameAs(target2);

            session.Eject(target2);

            (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task eject_a_document_clears_it_from_the_identity_map_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.DirtyTrackedSession())
        {
            session.Store(target1, target2);

            (await session.LoadAsync<Target>(target1.Id)).ShouldBeSameAs(target1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldBeSameAs(target2);

            session.Eject(target2);

            (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task eject_a_document_clears_it_from_the_unit_of_work_regular()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);

            session.Eject(target2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Single()
                .Id.ShouldBe(target1.Id);
        }
    }

    [Fact]
    public async Task eject_a_document_clears_it_from_the_unit_of_work_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.DirtyTrackedSession())
        {
            session.Store(target1, target2);

            session.Eject(target2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Single()
                .Id.ShouldBe(target1.Id);
        }
    }

    [Fact]
    public async Task eject_a_document_clears_it_from_the_unit_of_work_lightweight()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.LightweightSession())
        {
            session.Store(target1, target2);

            session.Eject(target2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().Single()
                .Id.ShouldBe(target1.Id);
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_it_from_the_identity_map_regular()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using var session = theStore.IdentitySession();
        session.Store(target1, target2);
        session.Store(user1, user2);

        (await session.LoadAsync<Target>(target1.Id)).ShouldBeSameAs(target1);
        (await session.LoadAsync<Target>(target2.Id)).ShouldBeSameAs(target2);
        (await session.LoadAsync<User>(user1.Id)).ShouldBeSameAs(user1);
        (await session.LoadAsync<User>(user2.Id)).ShouldBeSameAs(user2);

        session.EjectAllOfType(typeof(Target));

        session.PendingChanges.OperationsFor<User>().Any().ShouldBeTrue();
        session.PendingChanges.OperationsFor<Target>().Any().ShouldBeFalse();

        await session.SaveChangesAsync();

        (await session.LoadAsync<Target>(target1.Id)).ShouldBeNull();
        (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();
        (await session.LoadAsync<User>(user1.Id)).ShouldBeSameAs(user1);
        (await session.LoadAsync<User>(user2.Id)).ShouldBeSameAs(user2);
    }

    [Fact]
    public async Task eject_a_document_type_clears_it_from_the_identity_map_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using var session = theStore.DirtyTrackedSession();
        session.Store(target1, target2);
        session.Store(user1, user2);

        (await session.LoadAsync<Target>(target1.Id)).ShouldBeSameAs(target1);
        (await session.LoadAsync<Target>(target2.Id)).ShouldBeSameAs(target2);
        (await session.LoadAsync<User>(user1.Id)).ShouldBeSameAs(user1);
        (await session.LoadAsync<User>(user2.Id)).ShouldBeSameAs(user2);

        session.EjectAllOfType(typeof(Target));

        session.PendingChanges.OperationsFor<User>().Any().ShouldBeTrue();
        session.PendingChanges.OperationsFor<Target>().Any().ShouldBeFalse();

        (await session.LoadAsync<Target>(target1.Id)).ShouldBeNull();
        (await session.LoadAsync<Target>(target2.Id)).ShouldBeNull();
        (await session.LoadAsync<User>(user1.Id)).ShouldBeSameAs(user1);
        (await session.LoadAsync<User>(user2.Id)).ShouldBeSameAs(user2);
    }

    [Fact]
    public async Task eject_a_document_type_clears_it_from_the_unit_of_work_regular()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using (var session = theStore.IdentitySession())
        {
            session.Insert(target1);
            session.Store(target2);
            session.Insert(user1);
            session.Store(user2);

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().ShouldBeEmpty();
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull();
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_updates_from_the_unit_of_work_regular()
    {
        var target1 = new Target { Number = 1 };
        var target2 = new Target { Number = 2 };
        var user1 = new User { Age = 10 };
        var user2 = new User { Age = 20 };

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);
            session.Store(user1, user2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.IdentitySession())
        {
            target1.Number = 3;
            target2.Number = 4;
            user1.Age = 30;
            user2.Age = 40;
            session.Update(target1);
            session.Update(target2);
            session.Update(user1);
            session.Update(user2);

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Target>(target1.Id)).ShouldNotBeNull().Number.ShouldBe(1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldNotBeNull().Number.ShouldBe(2);
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull().Age.ShouldBe(30);
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull().Age.ShouldBe(40);
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_it_from_the_unit_of_work_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using (var session = theStore.DirtyTrackedSession())
        {
            session.Insert(target1);
            session.Store(target2);
            session.Insert(user1);
            session.Store(user2);

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().ShouldBeEmpty();
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull();
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_updates_from_the_unit_of_work_dirty()
    {
        var target1 = new Target { Number = 1 };
        var target2 = new Target { Number = 2 };
        var user1 = new User { Age = 10 };
        var user2 = new User { Age = 20 };

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);
            session.Store(user1, user2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.DirtyTrackedSession())
        {
            // Need to reload the objects inside the dirty session for it to know about them
            (await session.LoadAsync<Target>(target1.Id))!.Number = 3;
            (await session.LoadAsync<Target>(target2.Id))!.Number = 4;
            (await session.LoadAsync<User>(user1.Id))!.Age = 30;
            (await session.LoadAsync<User>(user2.Id))!.Age = 40;

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Target>(target1.Id)).ShouldNotBeNull().Number.ShouldBe(1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldNotBeNull().Number.ShouldBe(2);
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull().Age.ShouldBe(30);
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull().Age.ShouldBe(40);
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_it_from_the_unit_of_work_lightweight()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using (var session = theStore.LightweightSession())
        {
            session.Insert(target1);
            session.Store(target2);
            session.Insert(user1);
            session.Store(user2);

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Query<Target>().ShouldBeEmpty();
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull();
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task eject_a_document_type_clears_updates_from_the_unit_of_work_lightweight()
    {
        var target1 = new Target { Number = 1 };
        var target2 = new Target { Number = 2 };
        var user1 = new User { Age = 10 };
        var user2 = new User { Age = 20 };

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);
            session.Store(user1, user2);

            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            target1.Number = 3;
            target2.Number = 4;
            user1.Age = 30;
            user2.Age = 40;
            session.Update(target1);
            session.Update(target2);
            session.Update(user1);
            session.Update(user2);

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<Target>(target1.Id)).ShouldNotBeNull().Number.ShouldBe(1);
            (await session.LoadAsync<Target>(target2.Id)).ShouldNotBeNull().Number.ShouldBe(2);
            (await session.LoadAsync<User>(user1.Id)).ShouldNotBeNull().Age.ShouldBe(30);
            (await session.LoadAsync<User>(user2.Id)).ShouldNotBeNull().Age.ShouldBe(40);
        }
    }

    public ejecting_documents(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
