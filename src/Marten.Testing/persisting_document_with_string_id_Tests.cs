using Shouldly;

namespace Marten.Testing
{
    public class Account
    {
        public string Id { get;set; }
    }

    public class persisting_document_with_string_id_Tests : DocumentSessionFixture
    {
        public void persist_and_load()
        {
            var account = new Account{Id = "email@server.com"};

            theSession.Store(account);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<Account>("email@server.com")
                    .ShouldNotBeNull();

                session.Load<Account>("nonexistent@server.com")
                    .ShouldBeNull();
            }

        }

        public void persist_and_delete()
        {
            var account = new Account { Id = "email@server.com" };

            theSession.Store(account);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Delete<Account>(account.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<Account>(account.Id)
                    .ShouldBeNull();
            }
        }
    }
}