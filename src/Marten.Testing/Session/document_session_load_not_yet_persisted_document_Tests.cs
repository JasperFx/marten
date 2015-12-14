using System;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Session
{
    public class not_tracked_document_session_load_not_yet_persisted_document_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void then_a_new_document_should_be_returned()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };

            theSession.Store(user1);

            var fromSession = theSession.Load<User>(user1.Id);

            fromSession.ShouldNotBeSameAs(user1);
        }

        [Fact]
        public void given_document_is_already_added_then_a_new_document_should_be_returned()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };

            theSession.Store(user1);
            theSession.Store(user1);

            var fromSession = theSession.Load<User>(user1.Id);

            fromSession.ShouldNotBeSameAs(user1);
        }

        [Fact]
        public void given_document_with_same_id_is_already_added_then_exception_should_occur()
        {
            var user1 = new User { FirstName = "Tim", LastName = "Cools" };
            var user2 = new User { FirstName = "Tim2", LastName = "Cools2", Id = user1.Id };

            theSession.Store(user1);
            theSession.Store(user2);
            theSession.SaveChanges();

            //the non tracked session doesn't verify whether changer are already added.
            //so no exception should be thrown
        }
    }
}