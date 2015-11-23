using System.Linq;
using Marten.Services;
using Shouldly;

namespace Marten.Testing
{
    public class LongDoc
    {
        public long Id { get; set; }
    }

    public class persist_and_load_documents_with_long_ids_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public void persist_and_load()
        {
            var LongDoc = new LongDoc { Id = 456 };

            theSession.Store(LongDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<LongDoc>(456)
                    .ShouldNotBeNull();

                session.Load<LongDoc>(222)
                    .ShouldBeNull();
            }

        }

        public void auto_assign_id_for_0_id()
        {
            var doc = new LongDoc { Id = 0 };

            theSession.Store(doc);

            doc.Id.ShouldBeGreaterThan(0L);

            var doc2 = new LongDoc { Id = 0 };
            theSession.Store(doc2);

            doc2.Id.ShouldNotBe(0L);

            doc2.Id.ShouldNotBe(doc.Id);
        }

        public void persist_and_delete()
        {
            var LongDoc = new LongDoc { Id = 567 };

            theSession.Store(LongDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Delete<LongDoc>(LongDoc.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<LongDoc>(LongDoc.Id)
                    .ShouldBeNull();
            }
        }

        public void load_by_array_of_ids()
        {
            theSession.Store(new LongDoc{Id = 3});
            theSession.Store(new LongDoc{Id = 4});
            theSession.Store(new LongDoc{Id = 5});
            theSession.Store(new LongDoc{Id = 6});
            theSession.Store(new LongDoc{Id = 7});
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<LongDoc>().ById(4, 5, 6).Count().ShouldBe(3);
            }
        }

    }
}