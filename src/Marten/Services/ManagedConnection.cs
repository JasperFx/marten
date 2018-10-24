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
        private IRetryPolicy _retryPolicy;

        // keeping this for binary compatibility (but not used)
        [Obsolete("Use the method which includes IRetryPolicy instead")]
        public ManagedConnection(IConnectionFactory factory) : this(factory, new NulloRetryPolicy())
        {
        }

        public ManagedConnection(IConnectionFactory factory, IRetryPolicy retryPolicy) : this(factory, CommandRunnerMode.AutoCommit, retryPolicy)
        {
        }

        // keeping this for binary compatibility (but not used)
        [Obsolete("Use the method which includes IRetryPolicy instead")]
        public ManagedConnection(SessionOptions options, CommandRunnerMode mode) : this(options, mode, new NulloRetryPolicy())
        {
        }

        public ManagedConnection(SessionOptions options, CommandRunnerMode mode, IRetryPolicy retryPolicy)
        {
            _ownsConnection = options.OwnsConnection;
            _mode = options.OwnsTransactionLifecycle ? mode : CommandRunnerMode.External;
            _isolationLevel = options.IsolationLevel;
            _commandTimeout = options.Timeout;

            var conn = options.Connection ?? options.Transaction?.Connection;

            _connection = new TransactionState(_mode, _isolationLevel, _commandTimeout, conn, options.OwnsConnection, options.Transaction);
            _retryPolicy = retryPolicy;
        }

        // keeping this for binary compatibility (but not used)
        [Obsolete("Use the method which includes IRetryPolicy instead")]
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int commandTimeout = 30) : this(factory, mode,
            new NulloRetryPolicy(), isolationLevel, commandTimeout)
        {
        }


        // 30 is NpgsqlCommand.DefaultTimeout - ok to burn it to the call site?
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode, IRetryPolicy retryPolicy,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int commandTimeout = 30)
        {
            _factory = factory;
            _mode = mode;
            _isolationLevel = isolationLevel;
            _commandTimeout = commandTimeout;
            _ownsConnection = true;
            _retryPolicy = retryPolicy;

        }

        private void buildConnection()
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);

                _retryPolicy.Execute(() => _connection.Open());
            }
        }

        private async Task buildConnectionAsync(CancellationToken token)
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);

                await _retryPolicy.ExecuteAsync(async () => await _connection.OpenAsync(token), token);
            }
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public int RequestCount { get; private set; }

        public void Commit()
        {
            if (_mode == CommandRunnerMode.External) return;

            buildConnection();

            _retryPolicy.Execute(() => _connection.Commit());

            _connection.Dispose();
            _connection = null;
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (_mode == CommandRunnerMode.External) return;

            await buildConnectionAsync(token).ConfigureAwait(false);   
            await _retryPolicy.ExecuteAsync( async () => await _connection.CommitAsync(token).ConfigureAwait(false), token);

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
                _retryPolicy.Execute(() => _connection.Rollback());
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
                await _retryPolicy.ExecuteAsync(async () => await _connection.RollbackAsync(token).ConfigureAwait(false), token);
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
                _retryPolicy.Execute(() => action(cmd));
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
                _retryPolicy.Execute(() => action(cmd));
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
                var returnValue = _retryPolicy.Execute<T>(() => func(cmd));
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
                var returnValue = _retryPolicy.Execute<T>(() => func(cmd));
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
                await _retryPolicy.ExecuteAsync(async () => await action(cmd, token).ConfigureAwait(false), token);
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
            await _retryPolicy.ExecuteAsync(async () => await buildConnectionAsync(token).ConfigureAwait(false), token);

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
                var returnValue = await _retryPolicy.ExecuteAsync<T>(async () => await func(cmd, token).ConfigureAwait(false), token);
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
                var returnValue = await _retryPolicy.ExecuteAsync<T>(async () => await func(cmd, token).ConfigureAwait(false), token);
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