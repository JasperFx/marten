using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class document_session_delete_a_single_document_Tests : DocumentSessionFixture
    {
        public void persist_and_delete_a_document_by_entity()
        {
            var user = new User {FirstName = "Mychal", LastName = "Thompson"};
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Delete(user);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }

        public void persist_and_delete_a_document_by_id()
        {
            var user = new User { FirstName = "Mychal", LastName = "Thompson" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Delete<User>(user.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }
    }
}