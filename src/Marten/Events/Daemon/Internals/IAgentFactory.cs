using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten.Storage;
using Marten.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten.Events.Daemon.Internals;

public interface IAgentFactory
{
    IReadOnlyList<ISubscriptionAgent> BuildAgents(string projectionOrSubscriptionName, MartenDatabase database);
    IReadOnlyList<ISubscriptionAgent> BuildAllProjectionAgents(MartenDatabase database);
    ISubscriptionAgent BuildProjectionAgentForShard(string shardName, MartenDatabase database);
}

public class AgentFactory: IAgentFactory
{
    private readonly DocumentStore _store;

    public AgentFactory(DocumentStore store)
    {
        _store = store;
    }

    public IReadOnlyList<ISubscriptionAgent> BuildAgents(string projectionOrSubscriptionName, MartenDatabase database)
    {
        if (_store.Options.Projections.TryFindProjection(projectionOrSubscriptionName, out var source1))
        {
            var shards = source1.AsyncProjectionShards(_store);
            return shards.Select(shard => buildAgentForShard(database, shard)).ToList();
        }

        if (_store.Options.Projections.TryFindSubscription(projectionOrSubscriptionName, out var source2))
        {
            var shards = source2.AsyncProjectionShards(_store);
            return shards.Select(shard => buildAgentForShard(database, shard)).ToList();
        }

        throw new ArgumentOutOfRangeException(nameof(projectionOrSubscriptionName),
            $"No registered projection matches the name '{projectionOrSubscriptionName}'. Available names are {_store.Options.Projections.AllProjectionNames().Join(", ")}");
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

    private SubscriptionAgent buildAgentForShard(MartenDatabase database, AsyncProjectionShard shard)
    {
        var logger = _store.Options.LogFactory?.CreateLogger<SubscriptionAgent>() ?? _store.Options.DotNetLogger
            ?? NullLogger<SubscriptionAgent>.Instance;

        var metrics = new SubscriptionMetrics(_store.Options.OpenTelemetry.Meter, shard.Name, database.Identifier);

        if (shard.Source != null)
        {
            // RIGHT HERE, HAVE IProjectionSource build the
            //var execution = new GroupedProjectionExecution<ProjectionUpdateBatch, EventRangeGroup>(new GroupedProjectionRunner(shard, _store, database), logger);
            var execution = shard.Source.BuildExecution(shard, _store, database, logger);

            var options = shard.Source.Options;
            var loader = new EventLoader(_store, database, shard, options);
            var wrapped = new ResilientEventLoader(_store.Options.ResiliencePipeline, loader);

            return new SubscriptionAgent(shard.Name, options, _store.Events.TimeProvider, wrapped, execution,
                database.Tracker, metrics, logger);
        }

        if (shard.SubscriptionSource != null)
        {
            var subscription = shard.SubscriptionSource.Build(_store);
            var execution =
                new SubscriptionExecution(new SubscriptionRunner(shard.Name, subscription, _store, database), logger);
            var options = shard.SubscriptionSource.Options;
            var loader = new EventLoader(_store, database, shard, options);
            var wrapped = new ResilientEventLoader(_store.Options.ResiliencePipeline, loader);

            return new SubscriptionAgent(shard.Name, options, _store.Options.Events.TimeProvider, wrapped, execution,
                database.Tracker, metrics, logger);
        }

        throw new ArgumentOutOfRangeException(nameof(shard), "This shard has neither a subscription nor projection");
    }
}
