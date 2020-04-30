using System;
using Marten.Services;
using Xunit;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace Marten.Testing.TrackingSession
{
    public class document_session_load_not_found_then_stored_IdentityMap_Tests : document_session_load_not_found_then_stored_Tests<IdentityMap>
    {
        public document_session_load_not_found_then_stored_IdentityMap_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
    public class document_session_load_not_found_then_stored_DirtyChecking_Tests : document_session_load_not_found_then_stored_Tests<DirtyTrackingIdentityMap>
    {
        public document_session_load_not_found_then_stored_DirtyChecking_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public abstract class document_session_load_not_found_then_stored_Tests<T> : IntegrationContextWithIdentityMap<T> where T : IIdentityMap
    {
        [Fact]
        public void then_a_document_can_be_added_with_then_specified_id()
        {
            var id = Guid.NewGuid();

            var notFound = theSession.Load<User>(id);

            var replacement = new User { Id = id, FirstName = "Tim", LastName = "Cools" };

            theSession.Store(replacement);
        }

        protected document_session_load_not_found_then_stored_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
