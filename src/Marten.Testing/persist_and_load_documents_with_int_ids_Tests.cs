using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class IntDoc
    {
        public int Id { get; set; }
    }

    public class persist_and_load_documents_with_int_ids_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void persist_and_load()
        {
            var IntDoc = new IntDoc { Id = 456 };

            theSession.Store(IntDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Load<IntDoc>(456)
                    .ShouldNotBeNull();

                session.Load<IntDoc>(222)
                    .ShouldBeNull();
            }

        }

        [Fact]
        public void auto_assign_id_for_0_id()
        {
            var doc = new IntDoc {Id = 0};

            theSession.Store(doc);

            doc.Id.ShouldBeGreaterThan(0);

            var doc2 = new IntDoc {Id = 0};
            theSession.Store(doc2);

            doc2.Id.ShouldNotBe(0);

            doc2.Id.ShouldNotBe(doc.Id);
        }

        [Fact]
        public void persist_and_delete()
        {
            var IntDoc = new IntDoc { Id = 567 };

            theSession.Store(IntDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Delete<IntDoc>(IntDoc.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Load<IntDoc>(IntDoc.Id)
                    .ShouldBeNull();
            }
        }

        [Fact]
        public void load_by_array_of_ids()
        {
            theSession.Store(new IntDoc { Id = 3 });
            theSession.Store(new IntDoc { Id = 4 });
            theSession.Store(new IntDoc { Id = 5 });
            theSession.Store(new IntDoc { Id = 6 });
            theSession.Store(new IntDoc { Id = 7 });
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Load<IntDoc>().ById(4, 5, 6).Count().ShouldBe(3);
            }
        }
    }
}