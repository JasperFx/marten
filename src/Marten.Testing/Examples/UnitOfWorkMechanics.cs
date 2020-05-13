using System;
using System.Linq;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class UnitOfWorkMechanics
    {
        // SAMPLE: lightweight_document_session_uow
        public void lightweight_document_session(IDocumentStore store)
        {
            using (var session = store.LightweightSession())
            {
                var user = new User { FirstName = "Jeremy", LastName = "Miller" };

                // Manually adding the new user to the session
                session.Store(user);

                var existing = session.Query<User>().Single(x => x.FirstName == "Max");
                existing.Internal = false;

                // Manually marking an existing user as changed
                session.Store(existing);

                // Marking another existing User document as deleted
                session.Delete<User>(Guid.NewGuid());

                // Persisting the changes to the database
                session.SaveChanges();
            }
        }

        // ENDSAMPLE

        // SAMPLE: tracking_document_session_uow
        public void tracking_document_session(IDocumentStore store)
        {
            using (var session = store.DirtyTrackedSession())
            {
                var user = new User { FirstName = "Jeremy", LastName = "Miller" };

                // Manually adding the new user to the session
                session.Store(user);

                var existing = session.Query<User>().Single(x => x.FirstName == "Max");
                existing.Internal = false;

                // Marking another existing User document as deleted
                session.Delete<User>(Guid.NewGuid());

                // Persisting the changes to the database
                session.SaveChanges();
            }
        }

        // ENDSAMPLE
    }
}
