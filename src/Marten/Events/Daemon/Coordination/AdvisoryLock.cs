using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Storage;
using Medallion.Threading.Postgres;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon.Coordination;

internal class AdvisoryLock : IAsyncDisposable
{
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private readonly Dictionary<int, PostgresDistributedLockHandle> _handles = new();
    private readonly LightweightCache<int, PostgresDistributedLock> _distributedLockProviders;

    public AdvisoryLock(IMartenDatabase database, ILogger logger)
    {
        _database = database;
        _logger = logger;

        _distributedLockProviders = new LightweightCache<int, PostgresDistributedLock>(
            (lockId => new PostgresDistributedLock(new PostgresAdvisoryLockKey(lockId),
                ((MartenDatabase)_database).DataSource)));
    }

    public bool HasLock(int lockId)
    {
        return _handles.TryGetValue(lockId, out var handle) && !handle.HandleLostToken.IsCancellationRequested;
    }

    public async Task<bool> TryAttainLockAsync(int lockId, CancellationToken token)
    {
        var locker = _distributedLockProviders[lockId];
        var handle = await locker.TryAcquireAsync(cancellationToken: token).ConfigureAwait(false);
        if (handle is not null)
        {
            _handles[lockId] = handle;
            return true;
        }
        return false;
    }

    public async Task ReleaseLockAsync(int lockId)
    {
        if (_handles.Remove(lockId, out var handle))
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var i in _handles.Keys)
            {
                if (_handles.Remove(i, out var handle))
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to dispose of advisory locks for database {Identifier}",
                _database.Identifier);
        }
    }
}
