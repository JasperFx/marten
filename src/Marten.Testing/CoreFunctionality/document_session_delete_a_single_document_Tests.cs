using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public abstract class document_session_delete_a_single_document_Tests<T> : IntegrationContextWithIdentityMap<T>
        where T : IIdentityMap
    {
        [Fact]
        public void persist_and_delete_a_document_by_entity()
        {
            var user = new User {FirstName = "Mychal", LastName = "Thompson"};
            theSession.Store(user);
            theSession.SaveChanges();


            using (var session = theStore.OpenSession())
            {
                session.Delete(user);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }

        [Fact]
        public void persist_and_delete_a_document_by_id()
        {
            var user = new User {FirstName = "Mychal", LastName = "Thompson"};
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.Delete<User>(user.Id);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Load<User>(user.Id).ShouldBeNull();
            }
        }

        protected document_session_delete_a_single_document_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class delete_with_nullo_Tests : document_session_delete_a_single_document_Tests<NulloIdentityMap>
    {
        public delete_with_nullo_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class delete_with_identity_map_Tests : document_session_delete_a_single_document_Tests<IdentityMap>
    {
        public delete_with_identity_map_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class delete_with_dirty_tracking_identity_map_Tests :
        document_session_delete_a_single_document_Tests<DirtyTrackingIdentityMap>
    {
        public delete_with_dirty_tracking_identity_map_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
