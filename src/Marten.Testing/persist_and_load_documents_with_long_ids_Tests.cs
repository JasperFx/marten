using Shouldly;

namespace Marten.Testing
{
    public class LongDoc
    {
        public long Id { get; set; }
    }

    public class persist_and_load_documents_with_long_ids_Tests : DocumentSessionFixture
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
    }
}