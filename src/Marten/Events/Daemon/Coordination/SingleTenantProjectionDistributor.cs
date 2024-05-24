using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten.Events.Daemon.Coordination;

public class SingleTenantProjectionDistributor : IProjectionDistributor
{
    private readonly DocumentStore _store;
    private readonly Cache<IMartenDatabase, AdvisoryLock> _locks;

    public SingleTenantProjectionDistributor(DocumentStore store)
    {
        _store = store;

        var logger = _store.Options.LogFactory?.CreateLogger<AdvisoryLock>() ??
                     _store.Options.DotNetLogger ?? NullLogger<AdvisoryLock>.Instance;

        _locks = new(db => new AdvisoryLock(db, logger));
    }

    public ValueTask<IReadOnlyList<IProjectionSet>> BuildDistributionAsync()
    {
        var database = _store.Storage.Database;
        var projectionShards = _store.Options.Projections.AllShards();

        IReadOnlyList<IProjectionSet> sets = projectionShards.Select(shard =>
        {
            // Make deterministic for each projection name
            var lockId =
                Math.Abs($"{_store.Options.EventGraph.DatabaseSchemaName}:{shard.Name.Identity}"
                    .GetDeterministicHashCode()) + _store.Options.Projections.DaemonLockId;

            return new ProjectionSet(lockId, _store, (MartenDatabase)database,
                new[] { shard.Name });
        }).OrderBy(x => Random.Shared.NextDouble()).ToList();

        return ValueTask.FromResult(sets);
    }

    public virtual Task RandomWait(CancellationToken token)
    {
        return Task.Delay(Random.Shared.Next(0, 500).Milliseconds(), token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var advisoryLock in _locks)
        {
            try
            {
                await advisoryLock.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var logger = _store.Options.LogFactory?.CreateLogger<SingleTenantProjectionDistributor>() ??
                             _store.Options.DotNetLogger ?? NullLogger.Instance;

                logger.LogError(e, "Error while trying to dispose SingleTenantProjectionDistributor");
            }
        }
    }

    public bool HasLock(IProjectionSet set)
    {
        return _locks[set.Database].HasLock(set.LockId);
    }

    public Task<bool> TryAttainLockAsync(IProjectionSet set, CancellationToken token)
    {
        return _locks[set.Database].TryAttainLockAsync(set.LockId, token);
    }

    public Task ReleaseLockAsync(IProjectionSet set)
    {
        return _locks[set.Database].ReleaseLockAsync(set.LockId);
    }

    public async Task ReleaseAllLocks()
    {
        foreach (var @lock in _locks)
        {
            await @lock.DisposeAsync().ConfigureAwait(false);
        }

        _locks.ClearAll();
    }
}
