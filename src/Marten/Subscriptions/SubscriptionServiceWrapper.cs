using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Internals;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Subscriptions;

internal class SubscriptionServiceWrapper: SubscriptionBase
{
    private readonly ISubscription _subscription;

    public SubscriptionServiceWrapper(ISubscription subscription)
    {
        _subscription = subscription;
        SubscriptionName = _subscription.GetType().Name;
    }

    public override Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        return _subscription.ProcessEventsAsync(page, operations, cancellationToken);
    }
}

internal class ScopedSubscriptionServiceWrapper<T>: SubscriptionBase where T : ISubscription
{
    private readonly IServiceProvider _provider;

    public ScopedSubscriptionServiceWrapper(IServiceProvider provider)
    {
        _provider = provider;
        SubscriptionName = typeof(T).Name;
    }

    public override async Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        using var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;
        var subscription = sp.GetRequiredService<T>();

        await subscription.ProcessEventsAsync(page, operations, cancellationToken).ConfigureAwait(false);
    }
}
