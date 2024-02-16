using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Archiving;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon;

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

    public AsyncProjectionShard(IProjectionSource source): this(ShardName.All,
        source)
    {
    }

    public IProjectionSource Source { get; }

    public Type? StreamType { get; set; }

    public IReadOnlyList<Type> EventTypes { get; init; }

    public bool IncludeArchivedEvents { get; set; }

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
    public ShardName Name { get; }
}
