using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Daemon.Coordination;

public interface IProjectionDistributor : IAsyncDisposable
{
    ValueTask<IReadOnlyList<IProjectionSet>> BuildDistributionAsync();
    Task RandomWait(CancellationToken token);

    bool HasLock(IProjectionSet set);
    Task<bool> TryAttainLockAsync(IProjectionSet set, CancellationToken token);

    Task ReleaseLockAsync(IProjectionSet set);

    Task ReleaseAllLocks();
}
