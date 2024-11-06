using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events.Projections;
using Marten.Events.Archiving;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Subscriptions;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon;

public enum ShardRole
{
    Subscription,
    Projection
}

/// <summary>
///     Definition of a single projection shard to be executed asynchronously
/// </summary>
public class AsyncProjectionShard
{
    public AsyncProjectionShard(string shardName, IProjectionSource source)
    {
        Name = new ShardName(source.ProjectionName, shardName, source.ProjectionVersion);
        Source = source;
    }

    public AsyncProjectionShard(string shardName, ISubscriptionSource source)
    {
        Name = new ShardName(source.SubscriptionName, shardName, source.SubscriptionVersion);
        SubscriptionSource = source;
    }

    public ShardRole Role => Source != null ? ShardRole.Projection : ShardRole.Subscription;

    public ISubscriptionSource SubscriptionSource { get; }

    public AsyncProjectionShard(IProjectionSource source): this(ShardName.All,
        source)
    {
    }

    internal void OverrideProjectionName(string projectionName)
    {
        var name = new ShardName(projectionName, Name.Key);
        Name = name;
    }

    public IProjectionSource Source { get; }

    public Type? StreamType { get; set; }

    public IReadOnlyList<Type> EventTypes { get; init; }

    public bool IncludeArchivedEvents { get; set; }

    // TODO -- reuse this somewhere
    public IEnumerable<ISqlFragment> BuildFilters(DocumentStore store)
    {
        if (EventTypes.Any() && !EventTypes.Any(x => x.IsAbstract || x.IsInterface))
        {
            yield return new EventTypeFilter(store.Options.EventGraph, EventTypes);
        }

        if (StreamType != null)
        {
            yield return new AggregateTypeFilter(StreamType, store.Options.EventGraph);
        }

        if (!IncludeArchivedEvents)
        {
            yield return IsNotArchivedFilter.Instance;
        }
    }

    /// <summary>
    ///     The identity of this projection shard
    /// </summary>
    public ShardName Name { get; private set; }
}
