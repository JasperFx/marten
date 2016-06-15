using System;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Testing.Documents;
using Marten.Testing.Schema;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class UnitOfWorkTests : DocumentSessionFixture<NulloIdentityMap>
    {
        public override void Dispose()
        {
            base.Dispose();
            theSession.Dispose();
        }

        [Fact]
        public void update_mixed_document_types()
        {
            var user1 = new User();
            var user2 = new User();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            var uow = theStore.Advanced.CreateUnitOfWork();
            uow.StoreUpdates(user1, user2);
            uow.StoreUpdates(issue1, issue2);
            uow.StoreUpdates(company1, company2);

            using (var connection = theStore.Advanced.OpenConnection())
            {
                var batch = new UpdateBatch(theStore.Advanced.Options, new JsonNetSerializer(), connection, new VersionTracker());

                uow.ApplyChanges(batch);
            }

            using (var session2 = theStore.OpenSession())
            {
                session2.Query<User>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(user1.Id, user2.Id);
                session2.Query<Issue>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(issue1.Id, issue2.Id);
                session2.Query<Company>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(company1.Id, company2.Id);
            }


        }

    }
}