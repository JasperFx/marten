using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Baseline;
using Weasel.Postgresql;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Timer = System.Timers.Timer;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// Coordinate the async daemon in the case of hot/cold failover
    /// where only one node at a time should be running the async daemon
    /// </summary>
    internal class HotColdCoordinator: INodeCoordinator, ISingleQueryRunner, IDisposable
    {
        private readonly DaemonSettings _settings;
        private readonly ILogger _logger;
        private NpgsqlConnection _connection;
        private Timer _timer;
        private readonly Tenant _tenant;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private IProjectionDaemon _daemon;


        public HotColdCoordinator(IDocumentStore store, DaemonSettings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;
            _tenant = store.As<DocumentStore>().Tenancy.Default;
        }

        private void startPollingForOwnership()
        {
            _timer = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = _settings.LeadershipPollingTime
            };

            _timer.Elapsed += TimerOnElapsed;

            _timer.Start();
        }

        private async Task<bool> tryToAttainLockAndStartShards()
        {
            bool gotLock = false;

            NpgsqlConnection conn = null;

            try
            {
                conn = _tenant.Database.CreateConnection();
                await conn.OpenAsync(_cancellation.Token).ConfigureAwait(false);

                gotLock = (bool) await conn.CreateCommand("SELECT pg_try_advisory_lock(:id);")
                    .With("id", _settings.DaemonLockId)
                    .ExecuteScalarAsync(_cancellation.Token).ConfigureAwait(false);

                if (!gotLock)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                conn?.SafeDispose();

                _logger.LogError(e, "Error trying to attain the async daemon lock");
                return false;
            }

            if (gotLock)
            {
                _logger.LogInformation("Attained lock for the async daemon, attempting to start all shards");

                try
                {
                    await startAllProjections(conn).ConfigureAwait(false);
                    stopPollingForOwnership();

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failure while trying to start all async projection shards");
                }
            }
            else
            {
                _logger.LogDebug("Attempted to attain lock for async projections, but could not take leadership.");
            }

            if (_timer == null || !_timer.Enabled)
            {
                startPollingForOwnership();
            }
            else
            {
                _timer.Start();
            }

            return false;
        }

        private void TimerOnElapsed(object sender, ElapsedEventArgs e)
        {
            Task.Run(tryToAttainLockAndStartShards, _cancellation.Token);
        }

        private void stopPollingForOwnership()
        {
            _timer.Enabled = false;
            _timer.SafeDispose();
            _timer = null;
        }

        private Task startAllProjections(NpgsqlConnection conn)
        {
            _connection = conn;

            return _daemon.StartAllShards();
        }

        public Task Start(IProjectionDaemon daemon, CancellationToken token)
        {
            _daemon = daemon;
            startPollingForOwnership();
            return Task.CompletedTask;
        }

        public async Task Stop()
        {
            _cancellation.Cancel();

            if (_connection != null)
            {
                _connection.SafeDispose();
                if (_daemon != null)
                {
                    await _daemon.StopAll().ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _connection?.SafeDispose();
            _timer?.SafeDispose();
        }

        public async Task<T> Query<T>(ISingleQueryHandler<T> handler, CancellationToken cancellation)
        {
            try
            {
                var command = handler.BuildCommand();
                command.Connection = _connection;

                using var reader = await command.ExecuteReaderAsync(cancellation).ConfigureAwait(false);

                return await handler.HandleAsync(reader, cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Let the caller deal with retries
                await reopenConnectionIfNecessary(cancellation).ConfigureAwait(false);
                throw;
            }
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
                await _daemon.StopAll().ConfigureAwait(false);
                startPollingForOwnership();
            }
        }

        public async Task SingleCommit(DbCommand command, CancellationToken cancellation)
        {
            NpgsqlTransaction tx = null;

            try
            {
                tx = _connection.BeginTransaction();
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
    }
}
