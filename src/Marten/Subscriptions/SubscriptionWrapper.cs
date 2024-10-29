using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
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
        SubscriptionName = _subscription.GetType().Name;
    }

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        return _subscription.ProcessEventsAsync(page, controller, operations, cancellationToken);
    }
}

internal class ScopedSubscriptionServiceWrapper<T>: SubscriptionBase where T : ISubscription
{
    private readonly IServiceProvider _provider;

    public ScopedSubscriptionServiceWrapper(IServiceProvider provider)
    {
        _provider = provider;
        SubscriptionName = typeof(T).Name;

        var scope = _provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var subscription = sp.GetRequiredService<T>().As<SubscriptionBase>();
        IncludedEventTypes.AddRange(subscription.IncludedEventTypes);
        StreamType = subscription.StreamType;
        IncludeArchivedEvents = subscription.IncludeArchivedEvents;
        scope.SafeDispose();
    }

    public override async Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;

        try
        {
            var subscription = sp.GetRequiredService<T>();

            return await subscription.ProcessEventsAsync(page, controller, operations, cancellationToken).ConfigureAwait(false);
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
