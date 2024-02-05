using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Marten.Storage;

namespace Marten.Events.Daemon.New;

public interface IAgentFactory
{
    IReadOnlyList<ISubscriptionAgent> BuildAgentsForProjection(string projectionName, MartenDatabase database);
    IReadOnlyList<ISubscriptionAgent> BuildAllAgents(MartenDatabase database);
    ISubscriptionAgent BuildAgentForShard(string shardName, MartenDatabase database);
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
        var execution = new GroupedProjectionExecution(shard.Source, _store, database);
        var loader = new EventLoader(_store, database, shard, shard.Source.Options);
        var wrapped = new ResilientEventLoader(_store.Options.ResiliencePipeline, loader);

        return new SubscriptionAgent(shard.Name, shard.Source.Options, wrapped, execution, database.Tracker);
    }

    public IReadOnlyList<ISubscriptionAgent> BuildAllAgents(MartenDatabase database)
    {
        var shards = _store.Options.Projections.AllShards();
        return shards.Select(x => buildAgentForShard(database, x)).ToList();
    }

    public ISubscriptionAgent BuildAgentForShard(string shardName, MartenDatabase database)
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
