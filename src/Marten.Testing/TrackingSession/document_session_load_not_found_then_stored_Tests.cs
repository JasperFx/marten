using System;
using Marten.Services;
using Xunit;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace Marten.Testing.TrackingSession
{

    public class document_session_load_not_found_then_stored_Tests : IntegrationContext
    {
        [Theory]
        [SessionTypes]
        public void then_a_document_can_be_added_with_then_specified_id(DocumentTracking tracking)
        {
            DocumentTracking = tracking;

            var id = Guid.NewGuid();

            var notFound = theSession.Load<User>(id);

            var replacement = new User { Id = id, FirstName = "Tim", LastName = "Cools" };

            theSession.Store(replacement);
        }

        public document_session_load_not_found_then_stored_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
