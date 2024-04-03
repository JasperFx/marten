using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;

namespace Marten.Subscriptions;

public abstract class SubscriptionBase: EventFilterable, ISubscription, ISubscriptionSource
{
    protected SubscriptionBase()
    {
        SubscriptionName = GetType().NameInCode();
    }

    public string SubscriptionName { get; set; }
    public uint SubscriptionVersion { get; set; } = 1;

    public virtual ValueTask DisposeAsync()
    {
        return new ValueTask();
    }
    public abstract Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken);

    public AsyncOptions Options { get; } = new();

    ISubscription ISubscriptionSource.Build(DocumentStore store)
    {
        return this;
    }

    IReadOnlyList<AsyncProjectionShard> ISubscriptionSource.AsyncProjectionShards(DocumentStore store)
    {
        return new List<AsyncProjectionShard> { new ("All",this)
        {
            EventTypes = IncludedEventTypes,
            StreamType = StreamType,
            IncludeArchivedEvents = false
        } };
    }
}
