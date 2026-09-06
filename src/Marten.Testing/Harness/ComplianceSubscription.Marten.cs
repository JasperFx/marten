using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Subscriptions;

namespace JasperFx.Events.ComplianceTests;

/*
 * Marten's half of the shared compliance subscription.
 *
 * The compliance library owns the recording, the waiting and the subscription name; this partial
 * supplies the one thing that cannot be portable. Both products declare ISubscription with an
 * identical member -- Task<IChangeListener> ProcessEventsAsync(EventRange, ISubscriptionController,
 * IDocumentOperations, CancellationToken) -- but IChangeListener is a per-product type, so the
 * signature cannot be written once in the shared source.
 *
 * jasperfx#768 (marten#5343) deepened the suite past mere delivery, and the two guarantees it
 * added both land in this partial rather than in the library:
 *
 *  - Writes through the SUPPLIED session must be committed with the batch. The library builds the
 *    notes (NotesFor) and asserts on them; the operations.Store call has to happen here because
 *    the session type is Marten's.
 *  - The IChangeListener returned from ProcessEventsAsync must actually be called after the commit.
 *    This used to return NullChangeListener.Instance -- the documented "I do not need to be
 *    signalled" return -- which is exactly what the new facts exist to catch: a store that drops
 *    the listener, or hands out a session it never commits, passes every delivery fact in the suite
 *    while doing neither. Neither fact is gated, for that reason.
 *
 * IChangeListener is a per-product type, so the listener is declared here rather than shared; the
 * library exposes RecordCommitAsync as a public member precisely so a consumer can call it from a
 * nested type like this one.
 *
 * Lives beside MartenComplianceFixture rather than in EventSourcingTests because both assemblies
 * reference the source-only compliance package and therefore both compile the library's half of
 * this partial, so both need this half to satisfy it.
 */
public partial class ComplianceSubscription: ISubscription
{
    public Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        Record(page.Events);

        foreach (var note in NotesFor(page))
        {
            operations.Store(note);
        }

        return Task.FromResult<IChangeListener>(new Listener(this));
    }

    public ValueTask DisposeAsync() => default;

    private sealed class Listener: IChangeListener
    {
        private readonly ComplianceSubscription _subscription;

        public Listener(ComplianceSubscription subscription)
        {
            _subscription = subscription;
        }

        public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
            => _subscription.RecordCommitAsync();

        public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
            => Task.CompletedTask;
    }
}
