using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Coordination;

internal class AdvisoryLock : IAsyncDisposable
{
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private NpgsqlConnection _conn;
    private readonly List<int> _locks = new();

    public AdvisoryLock(IMartenDatabase database, ILogger logger)
    {
        _database = database;
        _logger = logger;
    }

    public bool HasLock(int lockId)
    {
        return _locks.Contains(lockId);
    }

    public async Task<bool> TryAttainLockAsync(int lockId, CancellationToken token)
    {
        if (_conn == null)
        {
            _conn = _database.CreateConnection();
            await _conn.OpenAsync(token).ConfigureAwait(false);
        }

        var attained = await _conn.TryGetGlobalLock(lockId, cancellation: token).ConfigureAwait(false);
        if (attained == AttainLockResult.Success)
        {
            _locks.Add(lockId);
            return true;
        }

        return false;
    }

    public async Task ReleaseLockAsync(int lockId)
    {
        if (_conn == null) return;

        var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(1.Seconds());

        await _conn.ReleaseGlobalLock(lockId, cancellation: cancellation.Token).ConfigureAwait(false);
        _locks.Remove(lockId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_conn == null) return;

        try
        {
            await _conn.CloseAsync().ConfigureAwait(false);
            await _conn.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error trying to dispose of advisory locks for database {Identifier}", _database.Identifier);
        }
    }
}
