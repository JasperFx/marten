using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.Internals;

namespace Marten.Subscriptions;

public interface ISubscription : IAsyncDisposable
{
    Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken);
}