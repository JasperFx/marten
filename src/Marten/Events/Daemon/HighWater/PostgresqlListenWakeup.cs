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
public sealed class PostgresqlListenWakeup(
    NpgsqlDataSource dataSource,
    ILogger logger,
    string channel = PostgresqlListenWakeup.DefaultChannel) : IDaemonWakeup
{
    /// <summary>
    /// The default PostgreSQL notification channel name used by Marten
    /// to signal that new events have been appended to the event store.
    /// </summary>
    public const string DefaultChannel = "mt_events_appended";

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private NpgsqlConnection? _connection;
    private Task? _receiveTask;
    private volatile bool _disposed;

    public async Task WaitAsync(TimeSpan timeout, CancellationToken token)
    {
        await ensureListeningAsync(token).ConfigureAwait(false);

        // Drain any accumulated signals so we don't spin through
        // multiple rapid iterations after a burst of notifications.
        while (_signal.CurrentCount > 0)
        {
            await _signal.WaitAsync(0, token).ConfigureAwait(false);
        }

        await _signal.WaitAsync(timeout, token).ConfigureAwait(false);
    }

    private async Task ensureListeningAsync(CancellationToken token)
    {
        if (_connection != null) return;

        await _connectionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connection != null) return;

            var conn = await dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
            conn.Notification += onNotification;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"LISTEN {channel}";
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            _connection = conn;

            logger.LogInformation("Listening on PostgreSQL channel '{Channel}' for event store notifications", channel);

            // Drive the notification dispatch loop on the wakeup's own lifetime
            // CTS (not the caller's token). The caller token only governs the
            // FIRST WaitAsync; tying the long-lived receive loop to it would
            // tear the listener down as soon as the first call returns.
            _receiveTask = Task.Run(() => receiveLoop(_lifetime.Token), _lifetime.Token);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task receiveLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !_disposed)
            {
                var conn = _connection;
                if (conn == null) return;

                try
                {
                    await conn.WaitAsync(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (_disposed) return;
                    logger.LogWarning(ex, "PostgreSQL LISTEN connection error, will reconnect on next poll cycle");

                    conn.Notification -= onNotification;
                    try { await conn.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }

                    _connection = null;
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private void onNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        _signal.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Signal the receive loop to exit before disposing the connection —
        // Npgsql throws NpgsqlOperationInProgressException if you Dispose a
        // connection that's still parked on conn.WaitAsync.
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // _lifetime may already be disposed if Dispose is called twice
            // by a defensive caller; nothing else to do.
        }

        // Best-effort wait for the receive loop to unwind. If the connector's
        // cancel is slow we'd rather risk a brief block here than throw out
        // of Dispose into a daemon shutdown path. No CT — we already cancelled
        // _lifetime above; this Wait is just a join barrier with a bound.
        try
        {
#pragma warning disable MA0040
            _receiveTask?.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore MA0040
        }
        catch
        {
            // Ignore — receive loop has its own swallows; this guards
            // pathological connector states only.
        }

        if (_connection != null)
        {
            _connection.Notification -= onNotification;
            try { _connection.Dispose(); } catch { /* connector may already be in a torn-down state */ }
            _connection = null;
        }

        _signal.Dispose();
        _connectionLock.Dispose();
        _lifetime.Dispose();
    }
}
