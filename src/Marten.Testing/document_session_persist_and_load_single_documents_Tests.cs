using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class document_session_persist_and_load_single_documents_with_nullo_Tests : document_session_persist_and_load_single_documents_Tests<NulloIdentityMap> { }
    public class document_session_persist_and_load_single_documents_with_identity_map_Tests : document_session_persist_and_load_single_documents_Tests<IdentityMap> { }
    public class document_session_persist_and_load_single_documents_with_dirty_tracking_Tests : document_session_persist_and_load_single_documents_Tests<DirtyTrackingIdentityMap> { }



    public class document_session_persist_and_load_single_documents_Tests<T> : DocumentSessionFixture<T> where T : IIdentityMap
    {
        public void persist_a_single_document()
        {
            var user = new User {FirstName = "Magic", LastName = "Johnson"};

            
            theSession.Store(user);

            theSession.SaveChanges();

            var runner = theContainer.GetInstance<ICommandRunner>();
            
            var json = runner.QueryScalar<string>("select data from mt_doc_user where id = '{0}'".ToFormat(user.Id));

            json.ShouldNotBeNull();

            var loadedUser = new JsonNetSerializer().FromJson<User>(json);

            user.ShouldNotBeSameAs(loadedUser);
            loadedUser.FirstName.ShouldBe(user.FirstName);
            loadedUser.LastName.ShouldBe(user.LastName);
            
        }

        public void persist_and_reload_a_document()
        {
            var user = new User { FirstName = "James", LastName = "Worthy" };

            // theSession is Marten's IDocumentSession service
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session2 = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session2.ShouldNotBeSameAs(theSession);

                var user2 = session2.Load<User>(user.Id);

                user.ShouldNotBeSameAs(user2);
                user2.FirstName.ShouldBe(user.FirstName);
                user2.LastName.ShouldBe(user.LastName);
            }
        }

        public async Task persist_and_reload_a_document_async()
        {
            var user = new User { FirstName = "James", LastName = "Worthy" };

            // theSession is Marten's IDocumentSession service
            theSession.Store(user);
            await theSession.SaveChangesAsync();

            using (var session2 = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                session2.ShouldNotBeSameAs(theSession);

                var user2 = await session2.LoadAsync<User>(user.Id);

                user.ShouldNotBeSameAs(user2);
                user2.FirstName.ShouldBe(user.FirstName);
                user2.LastName.ShouldBe(user.LastName);
            }
        }

        public void try_to_load_a_document_that_does_not_exist()
        {
            theSession.Load<User>(Guid.NewGuid()).ShouldBeNull();
        }

        public void load_by_id_array()
        {
            var user1 = new User { FirstName = "Magic", LastName = "Johnson" };
            var user2 = new User { FirstName = "James", LastName = "Worthy" };
            var user3 = new User { FirstName = "Michael", LastName = "Cooper" };
            var user4 = new User { FirstName = "Mychal", LastName = "Thompson" };
            var user5 = new User { FirstName = "Kurt", LastName = "Rambis" };

            theSession.Store(user1);
            theSession.Store(user2);
            theSession.Store(user3);
            theSession.Store(user4);
            theSession.Store(user5);
            theSession.SaveChanges();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                var users = session.Load<User>().ById(user2.Id, user3.Id, user4.Id);
                users.Count().ShouldBe(3);
            }
        }

        public async Task load_by_id_array_async()
        {
            var user1 = new User { FirstName = "Magic", LastName = "Johnson" };
            var user2 = new User { FirstName = "James", LastName = "Worthy" };
            var user3 = new User { FirstName = "Michael", LastName = "Cooper" };
            var user4 = new User { FirstName = "Mychal", LastName = "Thompson" };
            var user5 = new User { FirstName = "Kurt", LastName = "Rambis" };

            theSession.Store(user1);
            theSession.Store(user2);
            theSession.Store(user3);
            theSession.Store(user4);
            theSession.Store(user5);
            await theSession.SaveChangesAsync();

            using (var session = theContainer.GetInstance<IDocumentStore>().OpenSession())
            {
                var users = await session.Load<User>().ByIdAsync(user2.Id, user3.Id, user4.Id);
                users.Count().ShouldBe(3);
            }
        }
    }
}