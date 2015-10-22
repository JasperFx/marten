using System.Linq;
using Shouldly;

namespace Marten.Testing
{
    public class IntDoc
    {
        public int Id { get; set; }
    }

    public class persist_and_load_documents_with_int_ids_Tests : DocumentSessionFixture
    {
        public void persist_and_load()
        {
            var IntDoc = new IntDoc { Id = 456 };

            theSession.Store(IntDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<IntDoc>(456)
                    .ShouldNotBeNull();

                session.Load<IntDoc>(222)
                    .ShouldBeNull();
            }

        }

        public void persist_and_delete()
        {
            var IntDoc = new IntDoc { Id = 567 };

            theSession.Store(IntDoc);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Delete<IntDoc>(IntDoc.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<IntDoc>(IntDoc.Id)
                    .ShouldBeNull();
            }
        }

        public void load_by_array_of_ids()
        {
            theSession.Store(new IntDoc { Id = 3 });
            theSession.Store(new IntDoc { Id = 4 });
            theSession.Store(new IntDoc { Id = 5 });
            theSession.Store(new IntDoc { Id = 6 });
            theSession.Store(new IntDoc { Id = 7 });
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<IntDoc>().ById(4, 5, 6).Count().ShouldBe(3);
            }
        }
    }
}