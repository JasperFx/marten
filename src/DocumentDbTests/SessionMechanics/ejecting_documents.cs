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
            session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
            session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

            // Eject the 2nd document
            session.Eject(target2);

            // Now that 2nd document is no longer in the identity map
            session.Load<Target>(target2.Id).ShouldBeNull();

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            // The 2nd document was ejected before the session
            // was saved, so it was never persisted
            session.Load<Target>(target2.Id).ShouldBeNull();
        }
        #endregion
    }

    [Fact]
    public void eject_a_document_clears_it_from_the_identity_map_regular()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.IdentitySession())
        {
            session.Store(target1, target2);

            session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
            session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

            session.Eject(target2);

            session.Load<Target>(target2.Id).ShouldBeNull();
        }
    }

    [Fact]
    public void eject_a_document_clears_it_from_the_identity_map_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();

        using (var session = theStore.DirtyTrackedSession())
        {
            session.Store(target1, target2);

            session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
            session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);

            session.Eject(target2);

            session.Load<Target>(target2.Id).ShouldBeNull();
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

        session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
        session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);
        session.Load<User>(user1.Id).ShouldBeTheSameAs(user1);
        session.Load<User>(user2.Id).ShouldBeTheSameAs(user2);

        session.EjectAllOfType(typeof(Target));

        session.PendingChanges.OperationsFor<User>().Any().ShouldBeTrue();
        session.PendingChanges.OperationsFor<Target>().Any().ShouldBeFalse();

        await session.SaveChangesAsync();

        session.Load<Target>(target1.Id).ShouldBeNull();
        session.Load<Target>(target2.Id).ShouldBeNull();
        session.Load<User>(user1.Id).ShouldBeTheSameAs(user1);
        session.Load<User>(user2.Id).ShouldBeTheSameAs(user2);
    }

    [Fact]
    public void eject_a_document_type_clears_it_from_the_identity_map_dirty()
    {
        var target1 = Target.Random();
        var target2 = Target.Random();
        var user1 = new User();
        var user2 = new User();

        using var session = theStore.DirtyTrackedSession();
        session.Store(target1, target2);
        session.Store(user1, user2);

        session.Load<Target>(target1.Id).ShouldBeTheSameAs(target1);
        session.Load<Target>(target2.Id).ShouldBeTheSameAs(target2);
        session.Load<User>(user1.Id).ShouldBeTheSameAs(user1);
        session.Load<User>(user2.Id).ShouldBeTheSameAs(user2);

        session.EjectAllOfType(typeof(Target));

        session.PendingChanges.OperationsFor<User>().Any().ShouldBeTrue();
        session.PendingChanges.OperationsFor<Target>().Any().ShouldBeFalse();

        session.Load<Target>(target1.Id).ShouldBeNull();
        session.Load<Target>(target2.Id).ShouldBeNull();
        session.Load<User>(user1.Id).ShouldBeTheSameAs(user1);
        session.Load<User>(user2.Id).ShouldBeTheSameAs(user2);
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
            session.Load<User>(user1.Id).ShouldNotBeNull();
            session.Load<User>(user2.Id).ShouldNotBeNull();
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
            session.Load<Target>(target1.Id).ShouldNotBeNull().Number.ShouldBe(1);
            session.Load<Target>(target2.Id).ShouldNotBeNull().Number.ShouldBe(2);
            session.Load<User>(user1.Id).ShouldNotBeNull().Age.ShouldBe(30);
            session.Load<User>(user2.Id).ShouldNotBeNull().Age.ShouldBe(40);
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
            session.Load<User>(user1.Id).ShouldNotBeNull();
            session.Load<User>(user2.Id).ShouldNotBeNull();
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
            session.Load<Target>(target1.Id)!.Number = 3;
            session.Load<Target>(target2.Id)!.Number = 4;
            session.Load<User>(user1.Id)!.Age = 30;
            session.Load<User>(user2.Id)!.Age = 40;

            session.EjectAllOfType(typeof(Target));

            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            session.Load<Target>(target1.Id).ShouldNotBeNull().Number.ShouldBe(1);
            session.Load<Target>(target2.Id).ShouldNotBeNull().Number.ShouldBe(2);
            session.Load<User>(user1.Id).ShouldNotBeNull().Age.ShouldBe(30);
            session.Load<User>(user2.Id).ShouldNotBeNull().Age.ShouldBe(40);
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
            session.Load<User>(user1.Id).ShouldNotBeNull();
            session.Load<User>(user2.Id).ShouldNotBeNull();
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
            session.Load<Target>(target1.Id).ShouldNotBeNull().Number.ShouldBe(1);
            session.Load<Target>(target2.Id).ShouldNotBeNull().Number.ShouldBe(2);
            session.Load<User>(user1.Id).ShouldNotBeNull().Age.ShouldBe(30);
            session.Load<User>(user2.Id).ShouldNotBeNull().Age.ShouldBe(40);
        }
    }

    public ejecting_documents(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
