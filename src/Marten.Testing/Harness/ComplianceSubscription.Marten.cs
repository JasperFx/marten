using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
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
 * NullChangeListener.Instance is the documented "I do not need to be signalled" return on both
 * products; it is spelled the same in each, but on each product's own type.
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

        return Task.FromResult(NullChangeListener.Instance);
    }

    public ValueTask DisposeAsync() => default;
}
