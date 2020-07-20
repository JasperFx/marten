using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.TrackingSession
{

    public class document_session_load_already_loaded_document_Tests : IntegrationContext
    {
        [Theory]
        [SessionTypes]
        public void when_loading_then_the_document_should_be_returned(DocumentTracking tracking)
        {
            DocumentTracking = tracking;

            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                var first = session.Load<User>(user.Id);
                var second = session.Load<User>(user.Id);

                first.ShouldBeSameAs(second);
            }
        }

        [Theory]
        [SessionTypes]
        public void when_loading_by_ids_then_the_same_document_should_be_returned(DocumentTracking tracking)
        {
            DocumentTracking = tracking;

            var user = new User { FirstName = "Tim", LastName = "Cools" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                var first = session.Load<User>(user.Id);
                var second = session.LoadMany<User>(user.Id)
                    .SingleOrDefault();

                first.ShouldBeSameAs(second);
            }
        }

        public document_session_load_already_loaded_document_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
