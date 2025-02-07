using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Descriptions;
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

/// <summary>
/// Base class for custom subscriptions for Marten event data
/// </summary>
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

    /// <summary>
    /// How to process events
    /// </summary>
    /// <param name="page"></param>
    /// <param name="controller"></param>
    /// <param name="operations"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public abstract Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken);

    ISubscription ISubscriptionSource.Build(DocumentStore store)
    {
        return buildSubscription(store);
    }

    /// <summary>
    /// Build the actual subscription object. By default, this just returns itself
    /// </summary>
    /// <param name="store"></param>
    /// <returns></returns>
    protected virtual ISubscription buildSubscription(DocumentStore store) => this;

    IReadOnlyList<AsyncProjectionShard> ISubscriptionSource.AsyncProjectionShards(DocumentStore store)
    {
        return new List<AsyncProjectionShard> { new ("All",this)
        {
            EventTypes = IncludedEventTypes,
            StreamType = StreamType,
            IncludeArchivedEvents = IncludeArchivedEvents
        } };
    }

    /// <summary>
    /// Descriptive name for Marten progress tracking and rebuild/replays
    /// </summary>
    public string SubscriptionName { get; set; }

    /// <summary>
    /// If this value is greater than 1, it will be treated as an all new subscription and will played from zero
    /// when deployed
    /// </summary>
    public uint SubscriptionVersion { get; set; } = 1;

    /// <summary>
    /// Fine tune the behavior of this subscription at runtime
    /// </summary>
    [ChildDescription]
    public AsyncOptions Options { get; protected set; } = new();
}
