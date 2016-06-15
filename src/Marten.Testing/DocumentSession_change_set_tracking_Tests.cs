using System;
using System.Linq;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class DocumentSession_change_set_tracking_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void categorize_changes_inserts_and_deletions()
        {
            var target1 = new Target();
            var target2 = new Target();
            var target3 = new Target();

            var newDoc1 = new Target {Id = Guid.Empty};
            var newDoc2 = new Target {Id = Guid.Empty};

            theSession.Store(target1, target2, target3, newDoc1, newDoc2);

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            theSession.Delete<Target>(id1);
            theSession.Delete<Target>(id2);

            theSession.PendingChanges.UpdatesFor<Target>()
                .ShouldHaveTheSameElementsAs(target1, target2, target3);

            theSession.PendingChanges.InsertsFor<Target>()
                .ShouldHaveTheSameElementsAs(newDoc1, newDoc2);

            theSession.PendingChanges.DeletionsFor<Target>().OfType<DeleteById>().Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(id1, id2);

            theSession.LastCommit.ShouldBeNull();
            theSession.SaveChanges();

            // Everything should be cleared out
            theSession.PendingChanges.Updates().Any().ShouldBeFalse();
            theSession.PendingChanges.Inserts().Any().ShouldBeFalse();
            theSession.PendingChanges.Deletions().Any().ShouldBeFalse();

            theSession.LastCommit.Updated.ShouldHaveTheSameElementsAs(target1, target2, target3);
            theSession.LastCommit.Inserted.ShouldHaveTheSameElementsAs(newDoc1, newDoc2);
            theSession.LastCommit.Deleted.OfType<DeleteById>().Select(x => x.Id).ShouldHaveTheSameElementsAs(id1, id2);

            theSession.Store(new Target());
            theSession.SaveChanges();

            theSession.Commits.Count().ShouldBe(2);
            theSession.LastCommit.Updated.Count().ShouldBe(1);
        }
    }
}