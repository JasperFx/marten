using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Medallion.Threading.Postgres;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;

namespace Marten.Events.Daemon.Coordination;

internal class AdvisoryLock : IAdvisoryLock
{
    private readonly string _databaseName;
    private readonly ILogger _logger;
    private readonly Dictionary<int, PostgresDistributedLockHandle> _handles = new();
    private readonly LightweightCache<int, PostgresDistributedLock> _distributedLockProviders;

    public AdvisoryLock(NpgsqlDataSource dataSource, ILogger logger, string databaseName)
    {
        _logger = logger;

        _distributedLockProviders = new LightweightCache<int, PostgresDistributedLock>(
            (lockId => new PostgresDistributedLock(new PostgresAdvisoryLockKey(lockId),
                EnsurePrimaryWhenMultiHost(dataSource))));
        _databaseName = databaseName;
    }

    private static NpgsqlDataSource EnsurePrimaryWhenMultiHost(NpgsqlDataSource source)
    {
        if (source is NpgsqlMultiHostDataSource multiHostDataSource)
            return multiHostDataSource.WithTargetSession(TargetSessionAttributes.ReadWrite);

        return source;
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
        foreach (var i in _handles.Keys)
        {
            if (_handles.Remove(i, out var handle))
            {
                try
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Underlying connection is already closed and there's nothing to dispose
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error trying to dispose of advisory locks for database {Identifier}", _databaseName);
                }
            }
        }
    }
}
