using System.Diagnostics;
using System.Linq;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class document_session_load_already_loaded_document_Tests : DocumentSessionFixture
    {
        public void when_loading_then_the_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                var first = session.Load<User>(user.Id);
                var second = session.Load<User>(user.Id);

                first.ShouldBeSameAs(second);
            }
        }

        public void when_loading_by_ids_then_the_same_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentSession>())
            {
                var first = session.Load<User>(user.Id);
                var second = session.Load<User>()
                    .ById(user.Id)
                    .SingleOrDefault();

                first.ShouldBeSameAs(second);
            }
        }
    }
}