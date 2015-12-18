using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public abstract class document_session_delete_a_single_document_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        [Fact]
        public void persist_and_delete_a_document_by_entity()
        {
            var user = new User {FirstName = "Mychal", LastName = "Thompson"};
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Delete(user);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }

        [Fact]
        public void persist_and_delete_a_document_by_id()
        {
            var user = new User { FirstName = "Mychal", LastName = "Thompson" };
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Delete<User>(user.Id);
                session.SaveChanges();
            }

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }
    }

    public class delete_with_nullo_Tests : document_session_delete_a_single_document_Tests<NulloIdentityMap> { }
    public class delete_with_identity_map_Tests : document_session_delete_a_single_document_Tests<IdentityMap> { }
    public class delete_with_dirty_tracking_identity_map_Tests : document_session_delete_a_single_document_Tests<DirtyTrackingIdentityMap> { }
}