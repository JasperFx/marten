using System;
using FubuCore;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing
{
    public class document_session_persist_and_load_single_documents_Tests : DocumentSessionFixture
    {
        public void persist_a_single_document()
        {
            var user = new User {FirstName = "Magic", LastName = "Johnson"};

            
            theSession.Store(user);

            theSession.SaveChanges();

            using (var runner = new CommandRunner(ConnectionSource.ConnectionString))
            {
                var json = runner.QueryScalar<string>("select data from mt_doc_user where id = '{0}'".ToFormat(user.Id));

                json.ShouldNotBeNull();

                var loadedUser = new JsonNetSerializer().FromJson<User>(json);

                user.ShouldNotBeSameAs(loadedUser);
                loadedUser.FirstName.ShouldBe(user.FirstName);
                loadedUser.LastName.ShouldBe(user.LastName);
            }
        }

        public void persist_and_reload_a_document()
        {
            var user = new User { FirstName = "James", LastName = "Worthy" };

            // theSession is Marten's IDocumentSession service
            theSession.Store(user);
            theSession.SaveChanges();

            using (var session2 = theContainer.GetInstance<IDocumentSession>())
            {
                session2.ShouldNotBeSameAs(theSession);

                var user2 = session2.Load<User>(user.Id);

                user.ShouldNotBeSameAs(user2);
                user2.FirstName.ShouldBe(user.FirstName);
                user2.LastName.ShouldBe(user.LastName);
            }
        }

        public void try_to_load_a_document_that_does_not_exist()
        {
            theSession.Load<User>(Guid.NewGuid()).ShouldBeNull();
        }
    }
}