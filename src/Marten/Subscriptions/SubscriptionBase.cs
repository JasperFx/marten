using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;

namespace Marten.Subscriptions;

/// <summary>
///     Base class for custom subscriptions for Marten event data
/// </summary>
public abstract class SubscriptionBase: JasperFxSubscriptionBase<IDocumentOperations, IQuerySession, ISubscription>,
    ISubscription
{
    protected SubscriptionBase()
    {
        SubscriptionName = GetType().NameInCode();
    }

    /// <summary>
    ///     How to process events
    /// </summary>
    /// <param name="page"></param>
    /// <param name="controller"></param>
    /// <param name="operations"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken);
}
