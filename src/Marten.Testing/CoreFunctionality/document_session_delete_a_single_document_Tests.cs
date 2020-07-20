using System;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public sealed class document_session_delete_a_single_document_Tests : IntegrationContext
    {
        public document_session_delete_a_single_document_Tests(DefaultStoreFixture fixture): base(fixture)
        {
        }

        [Theory]
        [SessionTypes]
        public void persist_and_delete_a_document_by_entity(DocumentTracking tracking)
        {
            DocumentTracking = tracking;

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


        [Fact]
        public void persist_and_delete_by_id_documents_with_the_same_id()
        {
            var id = Guid.NewGuid();
            using (var session = theStore.OpenSession())
            {
                var user = new User { Id = id, FirstName = "Mychal", LastName = "Thompson"};
                session.Store(user);
                session.SaveChanges();

                var target = new Target {Id = id};
                session.Store(target);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Delete<User>(id);
                session.Delete<Target>(id);

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Load<User>(id).ShouldBeNull();
                session.Load<Target>(id).ShouldBeNull();
            }
        }
    }


}
