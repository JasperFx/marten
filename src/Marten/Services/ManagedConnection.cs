using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Baseline;
using Marten.Exceptions;
using Marten.Schema.Arguments;
using Marten.Util;
using Npgsql;
using IsolationLevel = System.Data.IsolationLevel;

namespace Marten.Services
{
    public class ManagedConnection: IManagedConnection
    {
        private readonly IConnectionFactory _factory;
        private readonly CommandRunnerMode _mode;
        private readonly IsolationLevel _isolationLevel;
        private readonly int? _commandTimeout;
        private TransactionState _connection;
        private readonly bool _ownsConnection;
        private readonly IRetryPolicy _retryPolicy;
        private readonly NpgsqlConnection _externalConnection;

        public ManagedConnection(IConnectionFactory factory, IRetryPolicy retryPolicy) : this(factory, CommandRunnerMode.AutoCommit, retryPolicy)
        {
        }

        public ManagedConnection(SessionOptions options, CommandRunnerMode mode, IRetryPolicy retryPolicy)
        {
            _ownsConnection = options.OwnsConnection;
            _mode = options.OwnsTransactionLifecycle ? mode : CommandRunnerMode.External;
            _isolationLevel = options.IsolationLevel;

            _externalConnection = options.Connection ?? options.Transaction?.Connection;
            _commandTimeout = options.Timeout ?? _externalConnection?.CommandTimeout;

            _connection = new TransactionState(_mode, _isolationLevel, _commandTimeout, _externalConnection, _ownsConnection, options.Transaction);
            _retryPolicy = retryPolicy;
        }

        // 30 is NpgsqlCommand.DefaultTimeout - ok to burn it to the call site?
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode, IRetryPolicy retryPolicy,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int? commandTimeout = null)
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
                _connection = _factory is null ?
                    new TransactionState(_mode, _isolationLevel, _commandTimeout, _externalConnection, _ownsConnection) :
                    new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);

                _retryPolicy.Execute(() => _connection.Open());
            }
        }

        private async Task buildConnectionAsync(CancellationToken token)
        {
            if (_connection == null)
            {
                _connection = _factory is null ?
                    new TransactionState(_mode, _isolationLevel, _commandTimeout, _externalConnection, _ownsConnection) :
                    new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout, _ownsConnection);

                await _retryPolicy.ExecuteAsync(() => _connection.OpenAsync(token), token);
            }
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public int RequestCount { get; private set; }

        public void Commit()
        {
            if (_mode == CommandRunnerMode.External)
                return;

            buildConnection();

            _retryPolicy.Execute(_connection.Commit);

            _connection.Dispose();
            _connection = null;
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (_mode == CommandRunnerMode.External)
                return;

            await buildConnectionAsync(token).ConfigureAwait(false);
            await _retryPolicy.ExecuteAsync(() => _connection.CommitAsync(token), token);

            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        public void Rollback()
        {
            if (_connection == null)
                return;
            if (_mode == CommandRunnerMode.External)
                return;

            try
            {
                _retryPolicy.Execute(_connection.Rollback);
            }
            catch (RollbackException e)
            {
                if (e.InnerException != null)
                    Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
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
            if (_connection == null)
                return;
            if (_mode == CommandRunnerMode.External)
                return;

            try
            {
                await _retryPolicy.ExecuteAsync(() => _connection.RollbackAsync(token), token);
            }
            catch (RollbackException e)
            {
                if (e.InnerException != null)
                    Logger.LogFailure(new NpgsqlCommand(), e.InnerException);
            }
            catch (Exception e)
            {
                Logger.LogFailure(new NpgsqlCommand(), e);
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
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
            if (InTransaction()) return;

            buildConnection();

            _connection.BeginTransaction();
        }

        public async Task BeginTransactionAsync(CancellationToken token)
        {
            if (InTransaction()) return;

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

            // TODO -- this is fine for now, but we might wanna do more to centralize the
            // exception transformations
            if (EventStreamUnexpectedMaxEventIdExceptionTransform.Instance.TryTransform(e,
                out var eventStreamUnexpectedMaxEventIdException))
            {
                throw eventStreamUnexpectedMaxEventIdException;
            }

            if (e is NpgsqlException)
            {
                throw MartenCommandExceptionFactory.Create(cmd, e);
            }
        }

        public int Execute(NpgsqlCommand cmd)
        {
            buildConnection();

            RequestCount++;

            _connection.Apply(cmd);

            try
            {
                var returnValue = _retryPolicy.Execute(cmd.ExecuteNonQuery);
                Logger.LogSuccess(cmd);

                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(cmd, e);
                throw;
            }
        }

        public DbDataReader ExecuteReader(NpgsqlCommand command)
        {
            buildConnection();

            _connection.Apply(command);

            RequestCount++;

            try
            {
                var returnValue = _retryPolicy.Execute<DbDataReader>(command.ExecuteReader);
                Logger.LogSuccess(command);
                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(command, e);
                throw;
            }
        }

        public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            _connection.Apply(command);

            Logger.OnBeforeExecute(command);

            RequestCount++;

            try
            {
                var reader = await _retryPolicy.ExecuteAsync(() => command.ExecuteReaderAsync(token), token);
                Logger.LogSuccess(command);

                return reader;
            }
            catch (Exception e)
            {
                handleCommandException(command, e);
                throw;
            }
        }


        public async Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            RequestCount++;

            _connection.Apply(command);

            Logger.OnBeforeExecute(command);

            try
            {
                var returnValue = await _retryPolicy.ExecuteAsync(() => command.ExecuteNonQueryAsync(token), token);
                Logger.LogSuccess(command);

                return returnValue;
            }
            catch (Exception e)
            {
                handleCommandException(command, e);
                throw;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
