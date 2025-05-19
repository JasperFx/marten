﻿using System.Linq;
using System.Threading.Tasks;
using Marten.Linq.SqlGeneration;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class UnitOfWork_PendingChanges_Functionality_Tests : IntegrationContext
{
    [Fact]
    public void pending_changes_from_store()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();

        session.Store(user1, user2);

        var target1 = new Target();
        var target2 = new Target();

        session.Store(target1, target2);


        session.PendingChanges.Updates().Count().ShouldBe(4);
        session.PendingChanges.Updates().ShouldContain(user1);
        session.PendingChanges.Updates().ShouldContain(user2);
        session.PendingChanges.Updates().ShouldContain(target1);
        session.PendingChanges.Updates().ShouldContain(target2);
    }

    [Fact]
    public void pending_changes_operations_from_store()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();
        var user3 = new User();
        var user4 = new User();

        session.Store(user1);
        session.Insert(user2);
        session.Update(user3);
        session.Delete(user4);

        var target1 = new Target();
        var target2 = new Target();
        var target3 = new Target();
        var target4 = new Target();

        session.Store(target1);
        session.Insert(target2);
        session.Update(target3);
        session.Delete(target4);

        session.PendingChanges.Operations().Count().ShouldBe(8);

        session.ShouldHaveUpsertFor(user1);
        session.ShouldHaveInsertFor(user2);
        session.ShouldHaveUpdateFor(user3);
        session.ShouldHaveDeleteFor(user4);

        session.ShouldHaveUpsertFor(target1);
        session.ShouldHaveInsertFor(target2);
        session.ShouldHaveUpdateFor(target3);
        session.ShouldHaveDeleteFor(target4);
    }



    [Fact]
    public void pending_changes_operations_by_generic_type()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();
        var user3 = new User();
        var user4 = new User();

        session.Store(user1);
        session.Insert(user2);
        session.Update(user3);
        session.Delete(user4);

        var target1 = new Target();
        var target2 = new Target();
        var target3 = new Target();
        var target4 = new Target();

        session.Store(target1);
        session.Insert(target2);
        session.Update(target3);
        session.Delete(target4);

        session.PendingChanges.OperationsFor<User>().Count().ShouldBe(4);
        session.ShouldHaveUpsertFor(user1);
        session.ShouldHaveInsertFor(user2);
        session.ShouldHaveUpdateFor(user3);
        session.ShouldHaveDeleteFor(user4);


        session.PendingChanges.OperationsFor<Target>().Count().ShouldBe(4);
        session.ShouldHaveUpsertFor(target1);
        session.ShouldHaveInsertFor(target2);
        session.ShouldHaveUpdateFor(target3);
        session.ShouldHaveDeleteFor(target4);
    }


    [Fact]
    public void pending_changes_operations_by_type()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();
        var user3 = new User();
        var user4 = new User();

        session.Store(user1);
        session.Insert(user2);
        session.Update(user3);
        session.Delete(user4);

        var target1 = new Target();
        var target2 = new Target();
        var target3 = new Target();
        var target4 = new Target();

        session.Store(target1);
        session.Insert(target2);
        session.Update(target3);
        session.Delete(target4);

        session.PendingChanges.OperationsFor(typeof(User)).Count().ShouldBe(4);

        session.ShouldHaveUpsertFor(user1);
        session.ShouldHaveInsertFor(user2);
        session.ShouldHaveUpdateFor(user3);
        session.ShouldHaveDeleteFor(user4);


        session.PendingChanges.OperationsFor(typeof(Target)).Count().ShouldBe(4);
        session.ShouldHaveUpsertFor(target1);
        session.ShouldHaveInsertFor(target2);
        session.ShouldHaveUpdateFor(target3);
        session.ShouldHaveDeleteFor(target4);
    }

    [Fact]
    public void pending_changes_by_type()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();

        session.Store(user1, user2);

        var target1 = new Target();
        var target2 = new Target();

        session.Store(target1, target2);


        session.PendingChanges.UpdatesFor<User>().ShouldHaveTheSameElementsAs(user1, user2);
        session.PendingChanges.UpdatesFor<Target>().ShouldHaveTheSameElementsAs(target1, target2);
    }

    [Fact]
    public void pending_deletions()
    {
        using var session = theStore.LightweightSession();
        var user1 = new User();
        var user2 = new User();

        session.Delete(user1);
        session.Delete<User>(user2.Id);

        var target1 = new Target();

        session.Delete(target1);

        session.PendingChanges.Deletions().Count().ShouldBe(3);



        session.PendingChanges.DeletionsFor(typeof(Target)).OfType<Deletion>().Single().Id.ShouldBe(target1.Id);

        session.PendingChanges.DeletionsFor<User>().Count().ShouldBe(2);
        session.PendingChanges.DeletionsFor<User>().OfType<Deletion>().Any(x => x.Id.Equals(user1.Id)).ShouldBeTrue();
        session.PendingChanges.DeletionsFor<User>().OfType<Deletion>().Any(x => x.Id.Equals(user2.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task pending_with_dirty_checks()
    {
        var user1 = new User();
        var user2 = new User();

        using (var session1 = theStore.LightweightSession())
        {
            session1.Store(user1, user2);
            await session1.SaveChangesAsync();
        }

        using (var session2 = theStore.DirtyTrackedSession())
        {
            var user12 = await session2.LoadAsync<User>(user1.Id);
            var user22 = await session2.LoadAsync<User>(user2.Id);
            user12.FirstName = "Hank";

            session2.PendingChanges.UpdatesFor<User>().Single()
                .ShouldBe(user12);

            session2.PendingChanges.Updates().Single().ShouldBe(user12);

            session2.PendingChanges.UpdatesFor<User>().ShouldNotContain(user22);
        }
    }



    public UnitOfWork_PendingChanges_Functionality_Tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
