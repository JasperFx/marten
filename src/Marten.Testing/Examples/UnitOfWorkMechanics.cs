using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class UnitOfWorkMechanics
{
    public async Task lightweight_document_session(IDocumentStore store)
    {
        #region sample_lightweight_document_session_uow
        await using var session = store.LightweightSession();
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
        await session.SaveChangesAsync();
        #endregion
    }

    public async Task tracking_document_session(IDocumentStore store)
    {
        #region sample_tracking_document_session_uow
        await using var session = store.DirtyTrackedSession();
        var user = new User { FirstName = "Jeremy", LastName = "Miller" };

        // Manually adding the new user to the session
        session.Store(user);

        var existing = session.Query<User>().Single(x => x.FirstName == "Max");
        existing.Internal = false;

        // Marking another existing User document as deleted
        session.Delete<User>(Guid.NewGuid());

        // Persisting the changes to the database
        await session.SaveChangesAsync();
        #endregion
    }
}
