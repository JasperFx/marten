using System.Collections.Generic;
using Marten.Events.Daemon;

namespace Marten.Subscriptions;

public interface ISubscriptionSource
{
    public AsyncOptions Options { get; }
    IReadOnlyList<AsyncProjectionShard> AsyncProjectionShards(DocumentStore store);
    ISubscription Build(DocumentStore store);

    public string SubscriptionName { get; }
    public uint SubscriptionVersion { get; }
}
