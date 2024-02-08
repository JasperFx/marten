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

        var random = new Random();

        IReadOnlyList<IProjectionSet> sets = projectionShards.Select(shard =>
        {
            return new ProjectionSet(_store.Options.Projections.DaemonLockId, _store, (MartenDatabase)database,
                new[] { shard.Name });
        }).OrderBy(x => random.NextDouble()).ToList();

        return ValueTask.FromResult(sets);
    }

    public virtual Task RandomWait(CancellationToken token)
    {
        return Task.Delay(new Random().Next(0, 500).Milliseconds(), token);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var advisoryLock in _locks)
        {
            await advisoryLock.DisposeAsync().ConfigureAwait(false);
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
}
