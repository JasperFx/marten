using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;

namespace Marten.Events.Daemon.Coordination;

public class SoloProjectionDistributor: IProjectionDistributor
{
    private readonly DocumentStore _store;

    public SoloProjectionDistributor(DocumentStore store)
    {
        _store = store;
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask();
    }

    public async ValueTask<IReadOnlyList<IProjectionSet>> BuildDistributionAsync()
    {
        var databases = await _store.Storage.AllDatabases().ConfigureAwait(false);
        return databases.OfType<MartenDatabase>().Select(db =>
        {
            var projectionOptions = _store.Options.Projections;
            return new ProjectionSet(projectionOptions.DaemonLockId, _store, db,
                projectionOptions.AllShards().Select(x => x.Name).ToList());
        }).ToList();
    }

    public Task RandomWait(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public bool HasLock(IProjectionSet set)
    {
        return true;
    }

    public Task<bool> TryAttainLockAsync(IProjectionSet set, CancellationToken token)
    {
        return Task.FromResult(true);
    }

    public Task ReleaseLockAsync(IProjectionSet set)
    {
        return Task.CompletedTask;
    }

    public Task ReleaseAllLocks()
    {
        return Task.CompletedTask;
    }
}
