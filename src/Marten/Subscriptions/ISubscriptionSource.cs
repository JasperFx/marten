using System.Collections.Generic;
using Marten.Events.Daemon;

namespace Marten.Subscriptions;

public interface ISubscriptionSource
{
    AsyncOptions Options { get; }
    IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store);
    ISubscription Build(DocumentStore store);

    string SubscriptionName { get; }
    uint SubscriptionVersion { get; }
}