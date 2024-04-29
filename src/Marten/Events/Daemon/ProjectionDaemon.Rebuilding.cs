using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.Progress;
using Marten.Events.Projections;
using Marten.Services;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon
{
    public Task RebuildProjectionAsync(string projectionName, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionName, 5.Minutes(), token);
    }

    public Task RebuildProjectionAsync<TView>(CancellationToken token)
    {
        return RebuildProjectionAsync<TView>(5.Minutes(), token);
    }

    public Task RebuildProjectionAsync(Type projectionType, CancellationToken token)
    {
        return RebuildProjectionAsync(projectionType, 5.Minutes(), token);
    }

    public Task RebuildProjectionAsync(Type projectionType, TimeSpan shardTimeout, CancellationToken token)
    {
        if (projectionType.CanBeCastTo<IProjection>())
        {
            var projectionName = projectionType.FullNameInCode();
            return RebuildProjectionAsync(projectionName, shardTimeout, token);
        }

        if (projectionType.CanBeCastTo<IProjectionSource>())
        {
            try
            {
                var projection = Activator.CreateInstance(projectionType);
                if (projection is IProjectionSource wrapper)
                    return RebuildProjectionAsync(wrapper.ProjectionName, shardTimeout, token);

                throw new ArgumentOutOfRangeException(nameof(projectionType),
                    $"Type {projectionType.FullNameInCode()} is not a valid projection type");
            }
            catch (Exception e)
            {
                throw new ArgumentOutOfRangeException(nameof(projectionType), e,
                    $"No public default constructor for projection type {projectionType.FullNameInCode()}, you may need to supply the projection name instead");
            }
        }

        // Assume this is an aggregate type name
        return RebuildProjectionAsync(projectionType.NameInCode(), shardTimeout, token);
    }

    public Task RebuildProjectionAsync(string projectionName, TimeSpan shardTimeout, CancellationToken token)
    {
        if (!_store.Options.Projections.TryFindProjection(projectionName, out var projection))
        {
            throw new ArgumentOutOfRangeException(nameof(projectionName),
                $"No registered projection matches the name '{projectionName}'. Available names are {_store.Options.Projections.AllProjectionNames().Join(", ")}");
        }

        return rebuildProjection(projection, shardTimeout, token);
    }

    public Task RebuildProjectionAsync<TView>(TimeSpan shardTimeout, CancellationToken token)
    {
        if (typeof(TView).CanBeCastTo(typeof(ProjectionBase)) && typeof(TView).HasDefaultConstructor())
        {
            var projection = (ProjectionBase)Activator.CreateInstance(typeof(TView))!;
            return RebuildProjectionAsync(projection.ProjectionName!, shardTimeout, token);
        }

        return RebuildProjectionAsync(typeof(TView).Name, shardTimeout, token);
    }

        // TODO -- ZOMG, this is awful
    private async Task rebuildProjection(IProjectionSource source, TimeSpan shardTimeout, CancellationToken token)
    {
        await Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var subscriptionName = source.ProjectionName;
        Logger.LogInformation("Starting to rebuild Projection {ProjectionName}@{DatabaseIdentifier}",
            subscriptionName, Database.Identifier);

        await stopRunningAgents(subscriptionName).ConfigureAwait(false);

        if (token.IsCancellationRequested) return;

        // Check now regardless
        await _highWater.CheckNowAsync().ConfigureAwait(false);

        // If there's no data, do nothing
        if (Tracker.HighWaterMark == 0)
        {
            Logger.LogInformation("Aborting projection rebuild because the high water mark is 0 (no event data)");
            return;
        }

        if (token.IsCancellationRequested) return;

        var agents = _factory.BuildAgents(subscriptionName, Database);

        foreach (var agent in agents)
        {
            Tracker.MarkAsRestarted(agent.Name);
        }

        // Teardown the current state
        await teardownExistingProjectionProgress(source, token, agents).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            return;
        }

        var mark = Tracker.HighWaterMark;

        // Is the shard count the optimal DoP here?
        await Parallel.ForEachAsync(agents,
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = agents.Count },
            async (agent, cancellationToken) =>
            {
                Tracker.MarkAsRestarted(agent.Name);

                await rebuildAgent(agent, mark, shardTimeout).ConfigureAwait(false);
            }).ConfigureAwait(false);

        foreach (var agent in agents)
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(shardTimeout);

            try
            {
                await agent.StopAndDrainAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error trying to stop and drain agent {Name} after rebuilding", agent.Name.Identity);
            }
        }
    }

    private async Task stopRunningAgents(string subscriptionName)
    {
        var running = CurrentAgents().Where(x => x.Name.ProjectionName == subscriptionName).ToArray();

        await _semaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);

        try
        {
            foreach (var agent in running)
            {
                await agent.HardStopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }


    private async Task teardownExistingProjectionProgress(IProjectionSource source, CancellationToken token,
        IReadOnlyList<ISubscriptionAgent> agents)
    {
        var sessionOptions = SessionOptions.ForDatabase(Database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = _store.LightweightSession(sessionOptions);
        source.Options.Teardown(session);

        foreach (var agent in agents)
        {
            session.QueueOperation(new DeleteProjectionProgress(_store.Events, agent.Name.Identity));
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.ProjectionName);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    public async Task PrepareForRebuildsAsync()
    {
        if (_highWater.IsRunning)
        {
            await _highWater.StopAsync().ConfigureAwait(false);
        }

        await _highWater.CheckNowAsync().ConfigureAwait(false);
    }

    public async Task RewindSubscriptionAsync(string subscriptionName, CancellationToken token, long? sequenceFloor = 0,
        DateTimeOffset? timestamp = null)
    {
        if (timestamp.HasValue)
        {
            sequenceFloor = await Database.FindEventStoreFloorAtTimeAsync(timestamp.Value, token).ConfigureAwait(false);
            if (sequenceFloor == null) return;
        }

        if (_cancellation.IsCancellationRequested) return;

        await stopRunningAgents(subscriptionName).ConfigureAwait(false);

        if (_cancellation.IsCancellationRequested) return;

        var agents = _factory.BuildAgents(subscriptionName, Database);

        await rewindSubscriptionProgress(subscriptionName, token, sequenceFloor, agents).ConfigureAwait(false);

        foreach (var agent in agents)
        {
            Tracker.MarkAsRestarted(agent.Name);
            var errorOptions = _store.Options.Projections.RebuildErrors;
            await agent.StartAsync(new SubscriptionExecutionRequest(sequenceFloor.Value, ShardExecutionMode.Continuous,
                errorOptions, this)).ConfigureAwait(false);
            agent.MarkHighWater(HighWaterMark());
        }
    }

    private async Task rewindAgentProgress(string shardName, CancellationToken token, long sequenceFloor)
    {
        var sessionOptions = SessionOptions.ForDatabase(Database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = _store.LightweightSession(sessionOptions);

        if (sequenceFloor > 0)
        {
            session.QueueSqlCommand($"insert into {_store.Options.EventGraph.ProgressionTable} (name, last_seq_id) values (?, ?) on conflict (name) do update set last_seq_id = ?", shardName, sequenceFloor, sequenceFloor);
        }

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private async Task rewindSubscriptionProgress(string subscriptionName, CancellationToken token, long? sequenceFloor,
        IReadOnlyList<ISubscriptionAgent> agents)
    {
        var sessionOptions = SessionOptions.ForDatabase(Database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = _store.LightweightSession(sessionOptions);

        foreach (var agent in agents)
        {
            if (sequenceFloor.Value == 0)
            {
                session.QueueSqlCommand($"delete from {_store.Options.EventGraph.ProgressionTable} where name = ?", agent.Name.Identity);
            }
            else
            {
                session.QueueSqlCommand($"update {_store.Options.EventGraph.ProgressionTable} set last_seq_id = ? where name = ?", sequenceFloor, agent.Name.Identity);
            }
        }

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == subscriptionName && x.EventSequence >= sequenceFloor);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }
}
