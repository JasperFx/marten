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

public class MultiTenantedProjectionDistributor: IProjectionDistributor
{
    private readonly DocumentStore _store;
    private readonly Cache<IMartenDatabase, AdvisoryLock> _locks;

    public MultiTenantedProjectionDistributor(DocumentStore store)
    {
        _store = store;

        var logger = _store.Options.LogFactory?.CreateLogger<AdvisoryLock>() ??
                     _store.Options.DotNetLogger ?? NullLogger<AdvisoryLock>.Instance;

        _locks = new(db => new AdvisoryLock(db, logger));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var advisoryLock in _locks)
        {
            await advisoryLock.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IReadOnlyList<IProjectionSet>> BuildDistributionAsync()
    {
        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        return databases.OfType<MartenDatabase>().Select(db =>
        {
            var projectionOptions = _store.Options.Projections;
            return new ProjectionSet(projectionOptions.DaemonLockId, _store, db,
                projectionOptions.AllShards().Select(x => x.Name).ToList());
        }).OrderBy(x => Random.Shared.NextDouble()).ToList();
    }

    public virtual Task RandomWait(CancellationToken token)
    {
        return Task.Delay(Random.Shared.Next(0, 500).Milliseconds(), token);
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
