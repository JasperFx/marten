using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.TrackingSession
{
    public class document_session_load_already_loaded_document_with_IdentityMap_Tests : document_session_load_already_loaded_document_Tests<IdentityMap> { }
    public class document_session_load_already_loaded_document_with_DirtyTracking_Tests : document_session_load_already_loaded_document_Tests<DirtyTrackingIdentityMap> { }

    public abstract class document_session_load_already_loaded_document_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        [Fact]
        public void when_loading_then_the_document_should_be_returned()
        {
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

        [Fact]
        public void when_loading_by_ids_then_the_same_document_should_be_returned()
        {
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
    }
}