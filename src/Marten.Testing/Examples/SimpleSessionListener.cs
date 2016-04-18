using System.Diagnostics;
using Baseline;

namespace Marten.Testing.Examples

{

    // SAMPLE: writing_custom_session_listener
    // DocumentSessionListenerBase is a helper abstract class in Marten
    // with empty implementations of each method you may find helpful
    public class SimpleSessionListener : DocumentSessionListenerBase
    {
        public override void BeforeSaveChanges(IDocumentSession session)
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


        }

        public override void AfterCommit(IDocumentSession session)
        {
            // See what was just persisted, and possibly carry out post
            // commit actions

            var last = session.LastCommit;

            last.Updated.Each(x => Debug.WriteLine($"{x} was updated"));
            last.Deleted.Each(x => Debug.WriteLine($"{x} was deleted"));
            last.Inserted.Each(x => Debug.WriteLine($"{x} was inserted"));
        }
    }

    // ENDSAMPLE
}