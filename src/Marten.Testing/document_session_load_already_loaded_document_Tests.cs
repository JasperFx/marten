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

            var first = theSession.Load<User>(user.Id);
            var second = theSession.Load<User>(user.Id);

            first.ShouldBeSameAs(second);
        }

        public void when_querying_then_the_document_should_be_returned()
        {
            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);

            var first = theSession.Load<User>(user.Id);
            var second = theSession.Query<User>()
                .FirstOrDefault(criteria => criteria.FirstName == "Tim");

            first.ShouldBeSameAs(second);
        }
    }
}