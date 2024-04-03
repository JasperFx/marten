using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon.Internals;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Subscriptions;

internal class SubscriptionWrapper: SubscriptionBase
{
    private readonly ISubscription _subscription;

    public SubscriptionWrapper(ISubscription subscription)
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
        var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            var subscription = sp.GetRequiredService<T>();

            await subscription.ProcessEventsAsync(page, operations, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (scope is IAsyncDisposable ad)
            {
                await ad.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.SafeDispose();
            }
        }


    }
}
