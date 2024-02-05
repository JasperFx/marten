#nullable enable
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events.Daemon;

/// <summary>
///     Coordinate the async daemon in the case of hot/cold failover
///     where only one node at a time should be running the async daemon
/// </summary>
internal sealed class HotColdCoordinator: INodeCoordinator, ISingleQueryRunner
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private readonly DaemonSettings _settings;
    private NpgsqlConnection? _connection;
    private PeriodicTimer? _periodicTimer;

    public HotColdCoordinator(IMartenDatabase database, DaemonSettings settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _database = database;
    }

    public Task Start(IProjectionDaemon daemon, CancellationToken token)
    {
        Daemon = daemon;
        startPollingForOwnership();
        return Task.CompletedTask;
    }

    public IProjectionDaemon? Daemon { get; private set; }

    public async Task Stop()
    {
#if NET8_0
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif

        if (_connection != null)
        {
            _connection.SafeDispose();
            if (Daemon != null)
            {
                await Daemon.StopAllAsync().ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _connection?.SafeDispose();
        _periodicTimer?.SafeDispose();
    }

    public async Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
    {
        try
        {
            var command = handler.BuildCommand();
            command.Connection = _connection;

            await using var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

            return await handler.HandleAsync(reader, cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Let the caller deal with retries
            await reopenConnectionIfNecessary(cancellation).ConfigureAwait(false);
            throw;
        }
    }

    public async Task SingleCommit(DbCommand command, CancellationToken cancellation)
    {
        NpgsqlTransaction? tx = null;

        try
        {
            tx = await _connection!.BeginTransactionAsync(cancellation).ConfigureAwait(false);
            command.Connection = _connection;

            await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
            await tx.CommitAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (tx != null)
            {
                await tx.RollbackAsync(cancellation).ConfigureAwait(false);
            }

            // Let the caller deal with retries
            await reopenConnectionIfNecessary(cancellation).ConfigureAwait(false);
            throw;
        }
        finally
        {
            tx?.SafeDispose();
        }
    }

    private void startPollingForOwnership()
    {
        _periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.LeadershipPollingTime));
#pragma warning disable MA0040
        _ = Task.Run(async () =>
        {
            while (await _periodicTimer.WaitForNextTickAsync(_cancellation.Token).ConfigureAwait(false))
            {
                var attained = await tryToAttainLockAndStartShards().ConfigureAwait(false);
                if (attained)
                    break;
            }
            _periodicTimer.Dispose();
        });
#pragma warning restore MA0040
    }

    public bool IsPrimary { get; private set; }

    private async Task<bool> tryToAttainLockAndStartShards()
    {
        NpgsqlConnection? conn = null;

        try
        {
            conn = _database.CreateConnection();
            await conn.OpenAsync(_cancellation.Token).ConfigureAwait(false);

            IsPrimary = (bool)(await conn.CreateCommand("SELECT pg_try_advisory_lock(:id);")
                .With("id", _settings.DaemonLockId)
                .ExecuteScalarAsync(_cancellation.Token).ConfigureAwait(false))!;

            if (!IsPrimary)
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException e)
        {
            conn?.SafeDispose();
            _logger.LogWarning(e,
                "Operation was cancelled whilst trying to attain the async daemon lock for database {Database}",
                _database.Identifier);

            return false;
        }
        catch (Exception e)
        {
            conn?.SafeDispose();

            _logger.LogError(e, "Error trying to attain the async daemon lock for database {Database}",
                _database.Identifier);
        }

        if (IsPrimary)
        {
            _logger.LogInformation(
                "Attained lock for the async daemon for database {Database}, attempting to start all shards",
                _database.Identifier);

            try
            {
                await startAllProjections(conn!).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failure while trying to start all async projection shards for database {Database}",
                    _database.Identifier);

                await Stop().ConfigureAwait(false);
            }
        }
        return false;
    }

    private Task startAllProjections(NpgsqlConnection conn)
    {
        _connection = conn;

        return Daemon!.StartAllShards();
    }

    private async Task reopenConnectionIfNecessary(CancellationToken cancellation)
    {
        if (_connection?.State == ConnectionState.Open)
        {
            return;
        }

        _connection?.SafeDispose();

        var restarted = await tryToAttainLockAndStartShards().ConfigureAwait(false);
        if (!restarted)
        {
            await Daemon!.StopAllAsync().ConfigureAwait(false);
            startPollingForOwnership();
        }
    }
}
