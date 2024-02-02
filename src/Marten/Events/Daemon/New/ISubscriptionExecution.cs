using System;

namespace Marten.Events.Daemon.New;

public interface ISubscriptionExecution: IAsyncDisposable
{
    void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent);
}