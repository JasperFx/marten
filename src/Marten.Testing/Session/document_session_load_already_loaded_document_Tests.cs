using System.Linq;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Session
{
    public class document_session_load_already_loaded_document_Tests : DocumentSessionFixture
    {
        public void when_loading_then_a_different_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = CreateSession())
            {
                var first = session.Load<User>(user.Id);
                var second = session.Load<User>(user.Id);

                first.ShouldNotBeSameAs(second);
            }
        }

        public void when_loading_by_ids_then_a_different_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = CreateSession())
            {
                var first = session.Load<User>(user.Id);
                var second = session.Load<User>()
                    .ById(user.Id)
                    .SingleOrDefault();

                first.ShouldNotBeSameAs(second);
            }
        }
    }
}