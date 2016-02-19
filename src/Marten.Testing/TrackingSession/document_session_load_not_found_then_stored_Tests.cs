using System;
using Marten.Services;
using Xunit;
using Marten.Testing.Documents;

namespace Marten.Testing.TrackingSession
{
    public class document_session_load_not_found_then_stored_IdentityMap_Tests : document_session_load_not_found_then_stored_Tests<IdentityMap> { }
    public class document_session_load_not_found_then_stored_DirtyChecking_Tests : document_session_load_not_found_then_stored_Tests<DirtyTrackingIdentityMap> { }

    public abstract class document_session_load_not_found_then_stored_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        [Fact]
        public void then_a_document_can_be_added_with_then_specified_id()
        {
            var id = Guid.NewGuid();

            var notFound = theSession.Load<User>(id);

            var replacement = new User { Id = id, FirstName = "Tim", LastName = "Cools" };

            theSession.Store(replacement);
        }
    }
}
