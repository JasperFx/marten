using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services.Events;
using Npgsql;

namespace Marten.Services
{
    public class ManagedConnection : IManagedConnection
    {
        private readonly IConnectionFactory _factory;
        private readonly CommandRunnerMode _mode;
        private readonly IsolationLevel _isolationLevel;
        private readonly int _commandTimeout;
        private TransactionState _connection;
        private bool _ownsConnection;

        public ManagedConnection(IConnectionFactory factory) : this(factory, CommandRunnerMode.AutoCommit)
        {
        }

        public ManagedConnection(SessionOptions options, CommandRunnerMode mode)
        {
            _ownsConnection = options.OwnsConnection;
            _mode = options.OwnsTransactionLifecycle ? mode : CommandRunnerMode.External;
            _isolationLevel = options.IsolationLevel;
            _commandTimeout = options.Timeout;

            var conn = options.Connection ?? options.Transaction?.Connection;

            _connection = new TransactionState(_mode, _isolationLevel, _commandTimeout, conn, options.OwnsConnection, options.Transaction);

        }


        // 30 is NpgsqlCommand.DefaultTimeout - ok to burn it to the call site?
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int commandTimeout = 30)
        {
            _factory = factory;
            _mode = mode;
            _isolationLevel = isolationLevel;
            _commandTimeout = commandTimeout;
            _ownsConnection = true;

        }

        private void buildConnection()
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);
                _connection.Open();
            }
        }

        private Task buildConnectionAsync(CancellationToken token)
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);
                return _connection.OpenAsync(token);
            }
            return Task.CompletedTask;
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public int RequestCount { get; private set; }

        public void Commit()
        {
            if (_mode == CommandRunnerMode.External) return;

            buildConnection();

            _connection.Commit();

            _connection.Dispose();
            _connection = null;
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (_mode == CommandRunnerMode.External) return;

            await buildConnectionAsync(token).ConfigureAwait(false);

            await _connection.CommitAsync(token).ConfigureAwait(false);

            _connection.Dispose();
            _connection = null;
        }

        public void Rollback()
        {
            if (_connection == null) return;
            if (_mode == CommandRunnerMode.External) return;

            try
            {
                _connection.Rollback();
            }
            catch (RollbackException e)
            {
                if (e.InnerException != null) Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
            }
            catch (Exception e)
            {
                Logger.LogFailure(new NpgsqlCommand(), e);
            }
            finally
            {
                _connection.Dispose();
                _connection = null;
            }
        }

        public async Task RollbackAsync(CancellationToken token)
        {
            if (_connection == null) return;
            if (_mode == CommandRunnerMode.External) return;

            try
            {
                await _connection.RollbackAsync(token).ConfigureAwait(false);
            }
            catch (RollbackException e)
            {
                if (e.InnerException != null) Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
            }
            catch (Exception e)
            {
                Logger.LogFailure(new NpgsqlCommand(), e);
            }
            finally
            {
                _connection.Dispose();
                _connection = null;
            }
        }

        public void BeginSession()
        {
            if (_isolationLevel == IsolationLevel.Serializable)
            {
                BeginTransaction();
            }
        }

        public void BeginTransaction()
        {
            buildConnection();

            _connection.BeginTransaction();
        }

        public async Task BeginTransactionAsync(CancellationToken token)
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            _connection.BeginTransaction();
        }

        public bool InTransaction()
        {
            return _connection?.Transaction != null;
        }

        public NpgsqlConnection Connection
        {
            get
            {
                buildConnection();

                return _connection.Connection;
            }
        }

        private void handleCommandException(NpgsqlCommand cmd, Exception e)
        {
            this.SafeDispose();
            Logger.LogFailure(cmd, e);

            if ((e as PostgresException)?.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                throw new ConcurrentUpdateException(e);
            }

            if (e is NpgsqlException)
            {
                throw new MartenCommandException(cmd, e);
            }
        }

        public void Execute(NpgsqlCommand cmd, Action<NpgsqlCommand> action = null)
        {
            buildConnection();

            RequestCount++;

            if (action == null)
            {
                action = c => c.ExecuteNonQuery();
            }

            _connection.Apply(cmd);
            try
            {
                action(cmd);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public void Execute(Action<NpgsqlCommand> action)
        {
            buildConnection();

            RequestCount++;

            var cmd = _connection.CreateCommand();
            try
            {
                action(cmd);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public T Execute<T>(Func<NpgsqlCommand, T> func)
        {
            buildConnection();

            RequestCount++;

            var cmd = _connection.CreateCommand();
            try
            {
                var returnValue = func(cmd);
                Logger.LogSuccess(cmd);
                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public T Execute<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, T> func)
        {
            buildConnection();

            RequestCount++;

            _connection.Apply(cmd);

            try
            {
                var returnValue = func(cmd);
                Logger.LogSuccess(cmd);
                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public async Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            RequestCount++;

            var cmd = _connection.CreateCommand();

            try
            {
                await action(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public async Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            RequestCount++;

            _connection.Apply(cmd);

            try
            {
                await action(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public async Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            RequestCount++;

            var cmd = _connection.CreateCommand();

            try
            {
                var returnValue = await func(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public async Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            RequestCount++;

            _connection.Apply(cmd);

            try
            {
                var returnValue = await func(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }

    public static class PostgresErrorCodes
    {
        public const string SerializationFailure = "40001";
    }
}