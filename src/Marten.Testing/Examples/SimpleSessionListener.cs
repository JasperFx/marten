using System.Diagnostics;
using JasperFx.Core;
using Marten.Services;
using Marten.Testing.Documents;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Testing.Examples;

#region sample_writing_custom_session_listener
// DocumentSessionListenerBase is a helper abstract class in Marten
// with empty implementations of each method you may find helpful
public class SimpleSessionListener: DocumentSessionListenerBase
{
    public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        // Use pending changes to preview what is about to be
        // persisted
        var pending = session.PendingChanges;

        // Careful here, Marten can only sort documents into "inserts" or "updates" based
        // on whether or not Marten had to assign a new Id to that document upon DocumentStore()
        pending.InsertsFor<User>()
            .Each(user => Debug.WriteLine($"New user: {user.UserName}"));

        pending.UpdatesFor<User>()
            .Each(user => Debug.WriteLine($"Updated user {user.UserName}"));

        pending.DeletionsFor<User>()
            .Each(d => Debug.WriteLine(d));

        // This is a convenience method to find all the pending events
        // organized into streams that will be appended to the event store
        pending.Streams()
            .Each(s => Debug.WriteLine(s));

        return Task.CompletedTask;
    }

    public override Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        // See what was just persisted, and possibly carry out post
        // commit actions

        var last = commit;

        last.Updated.Each(x => Debug.WriteLine($"{x} was updated"));
        last.Deleted.Each(x => Debug.WriteLine($"{x} was deleted"));
        last.Inserted.Each(x => Debug.WriteLine($"{x} was inserted"));

        return Task.CompletedTask;
    }
}

#endregion
