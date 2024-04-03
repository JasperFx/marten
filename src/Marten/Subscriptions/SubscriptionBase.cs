using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;

namespace Marten.Subscriptions;

public interface ISubscriptionOptions : IEventFilterable
{
    string SubscriptionName { get; set; }
    uint SubscriptionVersion { get; set; }
    AsyncOptions Options { get; }
}

public abstract class SubscriptionBase: EventFilterable, ISubscription, ISubscriptionSource, ISubscriptionOptions
{
    protected SubscriptionBase()
    {
        SubscriptionName = GetType().NameInCode();
    }

    public virtual ValueTask DisposeAsync()
    {
        return new ValueTask();
    }
    public abstract Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken);

    ISubscription ISubscriptionSource.Build(DocumentStore store)
    {
        return buildSubscription(store);
    }

    protected virtual ISubscription buildSubscription(DocumentStore store) => this;

    IReadOnlyList<AsyncProjectionShard> ISubscriptionSource.AsyncProjectionShards(DocumentStore store)
    {
        return new List<AsyncProjectionShard> { new ("All",this)
        {
            EventTypes = IncludedEventTypes,
            StreamType = StreamType,
            IncludeArchivedEvents = false
        } };
    }

    public string SubscriptionName { get; set; }
    public uint SubscriptionVersion { get; set; } = 1;
    public AsyncOptions Options { get; } = new();
}
