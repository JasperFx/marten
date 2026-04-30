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

internal class ScopedSubscriptionServiceWrapper<T>: SubscriptionBase where T : ISubscription
{
    private readonly IServiceProvider _provider;

    public ScopedSubscriptionServiceWrapper(IServiceProvider provider)
    {
        _provider = provider;
        Name = typeof(T).Name;

        var scope = _provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var subscription = sp.GetRequiredService<T>() as SubscriptionBase;
        if (subscription != null)
        {
            IncludedEventTypes.AddRange(subscription.IncludedEventTypes);
            StreamType = subscription.StreamType;
            IncludeArchivedEvents = subscription.IncludeArchivedEvents;

            // #4318: also propagate Name, Version, and Options that the inner
            // subscriber set in its constructor. Before this, settings like
            // `Options.BatchSize = 100` or `Options.SubscribeFromPresent()`
            // configured inside the user's SubscriptionBase ctor were silently
            // dropped on the Scoped registration path. The Singleton path
            // already worked because the registration's `configure` lambda
            // was applied directly to the resolved instance; for Scoped, the
            // wrapper itself is what the daemon reads, so its Options must
            // start from the inner subscriber's configured state.
            Options = subscription.Options;
            Name = subscription.Name;
            Version = subscription.Version;
        }
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
