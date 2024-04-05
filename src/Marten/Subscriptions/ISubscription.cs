using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;

namespace Marten.Subscriptions;

public interface ISubscription : IAsyncDisposable
{
    Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken);
}
