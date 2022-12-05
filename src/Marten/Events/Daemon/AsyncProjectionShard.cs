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

    /// <summary>
    ///     WHERE clause fragments used to filter the events
    ///     to be applied to this projection shard
    /// </summary>
    public ISqlFragment[] EventFilters { get; }

    /// <summary>
    ///     The identity of this projection shard
    /// </summary>
    public ShardName Name { get; }
}
