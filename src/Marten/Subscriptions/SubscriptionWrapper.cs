using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Subscriptions;

internal class SubscriptionWrapper: SubscriptionBase
{
    private readonly ISubscription _subscription;

    public SubscriptionWrapper(ISubscription subscription)
    {
        _subscription = subscription;
        Name = _subscription.GetType().Name;
    }

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        return _subscription.ProcessEventsAsync(page, controller, operations, cancellationToken);
    }
}
