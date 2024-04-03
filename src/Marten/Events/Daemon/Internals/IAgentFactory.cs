using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten.Events.Daemon.Internals;

public interface IAgentFactory
{
    IReadOnlyList<ISubscriptionAgent> BuildAgentsForProjection(string projectionName, MartenDatabase database);
    IReadOnlyList<ISubscriptionAgent> BuildAllProjectionAgents(MartenDatabase database);
    ISubscriptionAgent BuildProjectionAgentForShard(string shardName, MartenDatabase database);
}


public class AgentFactory : IAgentFactory
{
    private readonly DocumentStore _store;

    public AgentFactory(DocumentStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ISubscriptionAgent> BuildAgentsForProjection(string projectionName, MartenDatabase database)
    {
        if (!_store.Options.Projections.TryFindProjection(projectionName, out var projection))
        {
            throw new ArgumentOutOfRangeException(nameof(projectionName),
                $"No registered projection matches the name '{projectionName}'. Available names are {_store.Options.Projections.AllProjectionNames().Join(", ")}");
        }

        var shards = projection.AsyncProjectionShards(_store);
        return shards.Select(shard => buildAgentForShard(database, shard)).ToList();
    }

    private SubscriptionAgent buildAgentForShard(MartenDatabase database, AsyncProjectionShard shard)
    {
        var logger = _store.Options.LogFactory?.CreateLogger<SubscriptionAgent>() ?? _store.Options.DotNetLogger
            ?? NullLogger<SubscriptionAgent>.Instance;

        var execution = new GroupedProjectionExecution(shard, _store, database, logger);
        var loader = new EventLoader(_store, database, shard, shard.Source.Options);
        var wrapped = new ResilientEventLoader(_store.Options.ResiliencePipeline, loader);

        return new SubscriptionAgent(shard.Name, shard.Source.Options, wrapped, execution, database.Tracker, logger);
    }

    public IReadOnlyList<ISubscriptionAgent> BuildAllProjectionAgents(MartenDatabase database)
    {
        var shards = _store.Options.Projections.AllShards();
        return shards.Select(x => buildAgentForShard(database, x)).ToList();
    }

    public ISubscriptionAgent BuildProjectionAgentForShard(string shardName, MartenDatabase database)
    {
        var shard = _store.Options.Projections.AllShards().FirstOrDefault(x => x.Name.Identity == shardName);
        if (shard == null)
        {
            throw new ArgumentOutOfRangeException(nameof(shardName),
                $"Unknown shard name '{shardName}'. Value options are {_store.Options.Projections.AllShards().Select(x => x.Name.Identity).Join(", ")}");
        }

        return buildAgentForShard(database, shard);
    }
}
