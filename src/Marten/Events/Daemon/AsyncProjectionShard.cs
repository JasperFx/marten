using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Archiving;
using Marten.Events.Projections;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon;

/// <summary>
///     Definition of a single projection shard to be executed asynchronously
/// </summary>
public class AsyncProjectionShard
{
    // TODO -- don't inject filters here
    public AsyncProjectionShard(string shardName, IProjectionSource source, ISqlFragment[] filters)
    {
        Name = new ShardName(source.ProjectionName, shardName);
        EventFilters = filters.Concat(new ISqlFragment[] { IsNotArchivedFilter.Instance }).ToArray();
        Source = source;
    }

    public AsyncProjectionShard(IProjectionSource source, ISqlFragment[] filters): this(ShardName.All,
        source, filters)
    {
    }

    public IProjectionSource Source { get; }

    public Type? StreamType { get; set; }

    public IReadOnlyList<Type> EventTypes { get; init; }

    public bool IncludeArchivedEvents { get; set; }

    /// <summary>
    ///     WHERE clause fragments used to filter the events
    ///     to be applied to this projection shard
    /// </summary>
    [Obsolete]
    public ISqlFragment[] EventFilters { get; }

    /// <summary>
    ///     The identity of this projection shard
    /// </summary>
    public ShardName Name { get; }
}
