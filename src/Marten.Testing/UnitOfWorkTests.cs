using System.Linq;
using Marten.Linq;
using Marten.Services;
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
                var batch = new UpdateBatch(theStore.Advanced.Options, new JsonNetSerializer(), connection);

                uow.ApplyChanges(batch);
            }

            using (var session2 = theStore.OpenSession())
            {
                session2.Query<User>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(user1.Id, user2.Id);
                session2.Query<Issue>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(issue1.Id, issue2.Id);
                session2.Query<Company>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(company1.Id, company2.Id);
            }


        }


        [Fact]
        public void apply_updates_via_the_actual_document()
        {
            var stringDoc1 = new StringDoc {Id = "Foo"};
            var stringDoc2 = new StringDoc {Id = "Bar"};
            var user1 = new User();
            var user2 = new User();
            var int1 = new IntDoc {Id = 1};
            var int2 = new IntDoc {Id = 2};
            var long1 = new LongDoc {Id = 3};
            var long2 = new LongDoc {Id = 4};

            var uow1 = theStore.Advanced.CreateUnitOfWork();
            uow1.StoreUpdates(user1, user2);
            uow1.StoreUpdates(stringDoc1, stringDoc2);
            uow1.StoreUpdates(int1, int2);
            uow1.StoreUpdates(long1, long2);




            var batch1 = theStore.Advanced.CreateUpdateBatch();
            uow1.ApplyChanges(batch1);

            batch1.Connection.Dispose();


            var uow2 = theStore.Advanced.CreateUnitOfWork();
            uow2.DeleteEntity(stringDoc2);
            uow2.DeleteEntity(user2);
            uow2.DeleteEntity(int2);
            uow2.DeleteEntity(long2);
            var batch2 = theStore.Advanced.CreateUpdateBatch();
            uow2.ApplyChanges(batch2);

            batch2.Connection.Dispose();

            theSession.Query<StringDoc>().Single().Id.ShouldBe(stringDoc1.Id);
            theSession.Query<User>().Single().Id.ShouldBe(user1.Id);
            theSession.Query<IntDoc>().Single().Id.ShouldBe(int1.Id);
            theSession.Query<LongDoc>().Single().Id.ShouldBe(long1.Id);
        }




        [Fact]
        public void apply_updates_via_the_id()
        {
            var stringDoc1 = new StringDoc { Id = "Foo" };
            var stringDoc2 = new StringDoc { Id = "Bar" };
            var user1 = new User();
            var user2 = new User();
            var int1 = new IntDoc { Id = 1 };
            var int2 = new IntDoc { Id = 2 };
            var long1 = new LongDoc { Id = 3 };
            var long2 = new LongDoc { Id = 4 };

            var uow1 = theStore.Advanced.CreateUnitOfWork();

            uow1.StoreUpdates(user1, user2);
            uow1.StoreUpdates(stringDoc1, stringDoc2);
            uow1.StoreUpdates(int1, int2);
            uow1.StoreUpdates(long1, long2);
            var batch1 = theStore.Advanced.CreateUpdateBatch();
            uow1.ApplyChanges(batch1);

            batch1.Connection.Dispose();

            var uow2 = theStore.Advanced.CreateUnitOfWork();
            uow2.Delete<StringDoc>(stringDoc2.Id);
            uow2.Delete<User>(user2.Id);
            uow2.Delete<IntDoc>(int2.Id);
            uow2.Delete<LongDoc>(long2.Id);
            var batch2 = theStore.Advanced.CreateUpdateBatch();
            uow2.ApplyChanges(batch2);

            batch2.Connection.Dispose();

            theSession.Query<StringDoc>().Single().Id.ShouldBe(stringDoc1.Id);
            theSession.Query<User>().Single().Id.ShouldBe(user1.Id);
            theSession.Query<IntDoc>().Single().Id.ShouldBe(int1.Id);
            theSession.Query<LongDoc>().Single().Id.ShouldBe(long1.Id);
        }

    }
}