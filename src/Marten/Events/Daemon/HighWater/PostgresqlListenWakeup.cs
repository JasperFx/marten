using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon.HighWater;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
/// Uses PostgreSQL LISTEN/NOTIFY to wake the <see cref="HighWaterAgent"/>
/// immediately when new events are appended, instead of waiting for the
/// full polling interval. Falls back to the configured timeout if no
/// notification arrives.
/// </summary>
public sealed class PostgresqlListenWakeup : IHighWaterWakeup
{
    /// <summary>
    /// The default PostgreSQL notification channel name used by Marten
    /// to signal that new events have been appended to the event store.
    /// </summary>
    public const string DefaultChannel = "mt_events_appended";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger _logger;
    private readonly string _channel;
    private NpgsqlConnection? _connection;
    private readonly SemaphoreSlim _signal = new(0);
    private bool _disposed;

    public PostgresqlListenWakeup(NpgsqlDataSource dataSource, ILogger logger, string channel = DefaultChannel)
    {
        _dataSource = dataSource;
        _logger = logger;
        _channel = channel;
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken token)
    {
        await ensureListeningAsync(token).ConfigureAwait(false);

        // Wait for either a NOTIFY signal or the timeout
        try
        {
            await _signal.WaitAsync(timeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Timeout elapsed — this is normal, fall through to let the caller poll
        }
    }

    private async Task ensureListeningAsync(CancellationToken token)
    {
        if (_connection != null)
        {
            return;
        }

        _connection = await _dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
        _connection.Notification += onNotification;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"LISTEN {_channel}";
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        _logger.LogInformation("Listening on PostgreSQL channel '{Channel}' for event store notifications", _channel);

        // Start a background loop to consume notifications.
        // Npgsql requires WaitAsync to be called to receive notifications.
        _ = Task.Run(() => receiveLoop(token), token);
    }

    private async Task receiveLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                // WaitAsync blocks until a notification arrives or the timeout elapses.
                // We use a long timeout here as a keepalive; actual wakeup happens via _signal.
                await _connection!.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested || _disposed)
            {
                return;
            }
            catch (NpgsqlException ex)
            {
                _logger.LogWarning(ex, "PostgreSQL LISTEN connection error, will attempt to reconnect");
                await reconnectAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in PostgreSQL LISTEN receive loop");
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }
    }

    private void onNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        _signal.Release();
    }

    private async Task reconnectAsync(CancellationToken token)
    {
        try
        {
            if (_connection != null)
            {
                _connection.Notification -= onNotification;
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            await ensureListeningAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconnect PostgreSQL LISTEN connection");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            _connection.Notification -= onNotification;
            _connection.Dispose();
            _connection = null;
        }

        _signal.Dispose();
    }
}
