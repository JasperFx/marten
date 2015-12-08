using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Schema;
using Shouldly;
using StructureMap;

namespace Marten.Testing
{
    public class UnitOfWorkTests : IntegratedFixture
    {
        private readonly IDocumentSession theSession;

        public UnitOfWorkTests()
        {
            theSession = theContainer.GetInstance<IDocumentStore>().OpenSession();
        }

        public void update_mixed_document_types()
        {
            var user1 = new User ();
            var user2 = new User ();
            var issue1 = new Issue();
            var issue2 = new Issue();
            var company1 = new Company();
            var company2 = new Company();

            var uow = theContainer.GetInstance<UnitOfWork>();
            uow.Store(user1, user2);
            uow.Store(issue1, issue2);
            uow.Store(company1, company2);

            var batch = theContainer.GetInstance<UpdateBatch>();

            uow.ApplyChanges(batch);

            var theSession = theContainer.GetInstance<IDocumentStore>().OpenSession();

            theSession.Query<User>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(user1.Id, user2.Id);
            theSession.Query<Issue>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(issue1.Id, issue2.Id);
            theSession.Query<Company>().ToArray().Select(x => x.Id).ShouldHaveTheSameElementsAs(company1.Id, company2.Id);
        }

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

            var uow1 = theContainer.GetInstance<UnitOfWork>();
            uow1.Store(user1, user2);
            uow1.Store(stringDoc1, stringDoc2);
            uow1.Store(int1, int2);
            uow1.Store(long1, long2);
            var batch1 = theContainer.GetInstance<UpdateBatch>();
            uow1.ApplyChanges(batch1);


            var uow2 = theContainer.GetInstance<UnitOfWork>();
            uow2.Delete(stringDoc2);
            uow2.Delete(user2);
            uow2.Delete(int2);
            uow2.Delete(long2);
            var batch2 = theContainer.GetInstance<UpdateBatch>();
            uow2.ApplyChanges(batch2);

            theSession.Query<StringDoc>().Single().Id.ShouldBe(stringDoc1.Id);
            theSession.Query<User>().Single().Id.ShouldBe(user1.Id);
            theSession.Query<IntDoc>().Single().Id.ShouldBe(int1.Id);
            theSession.Query<LongDoc>().Single().Id.ShouldBe(long1.Id);
        }




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

            var uow1 = theContainer.GetInstance<UnitOfWork>();
            uow1.Store(user1, user2);
            uow1.Store(stringDoc1, stringDoc2);
            uow1.Store(int1, int2);
            uow1.Store(long1, long2);
            var batch1 = theContainer.GetInstance<UpdateBatch>();
            uow1.ApplyChanges(batch1);


            var uow2 = theContainer.GetInstance<UnitOfWork>();
            uow2.Delete<StringDoc>(stringDoc2.Id);
            uow2.Delete<User>(user2.Id);
            uow2.Delete<IntDoc>(int2.Id);
            uow2.Delete<LongDoc>(long2.Id);
            var batch2 = theContainer.GetInstance<UpdateBatch>();
            uow2.ApplyChanges(batch2);

            theSession.Query<StringDoc>().Single().Id.ShouldBe(stringDoc1.Id);
            theSession.Query<User>().Single().Id.ShouldBe(user1.Id);
            theSession.Query<IntDoc>().Single().Id.ShouldBe(int1.Id);
            theSession.Query<LongDoc>().Single().Id.ShouldBe(long1.Id);
        }

    }



}