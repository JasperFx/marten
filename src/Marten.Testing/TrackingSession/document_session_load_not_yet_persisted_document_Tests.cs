using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.TrackingSession
{
    public class document_session_load_not_yet_persisted_document_IdentityMap_Tests : document_session_load_not_yet_persisted_document_Tests<IdentityMap> { }
    public class document_session_load_not_yet_persisted_document_DirtyChecking_Tests : document_session_load_not_yet_persisted_document_Tests<DirtyTrackingIdentityMap> { }

    public abstract class document_session_load_not_yet_persisted_document_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        [Fact]
        public void then_the_document_should_be_returned()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };

            theSession.Store(user1);

            var fromSession = theSession.Load<User>(user1.Id);

            fromSession.ShouldBeSameAs(user1);
        }

        [Fact]
        public void given_document_is_already_added_then_document_should_be_returned()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };

            theSession.Store(user1);
            theSession.Store(user1);

            var fromSession = theSession.Load<User>(user1.Id);

            fromSession.ShouldBeSameAs(user1);
        }

        [Fact]
        public void given_document_with_same_id_is_already_added_then_exception_should_occur()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };
            var user2 = new User { FirstName = "Tim2", LastName = "Cools2", Id = user1.Id };

            theSession.Store(user1);

            Exception<InvalidOperationException>.ShouldBeThrownBy(() => theSession.Store(user2))
                .Message.ShouldBe("Document 'Marten.Testing.Documents.User' with same Id already added to the session.");
        }
    }
}