using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
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

        public ManagedConnection(IConnectionFactory factory) : this (factory, CommandRunnerMode.AutoCommit)
        {
        }

        // 30 is NpgsqlCommand.DefaultTimeout - ok to burn it to the call site?
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int commandTimeout = 30)
        {
            _factory = factory;
            _mode = mode;
            _isolationLevel = isolationLevel;
            _commandTimeout = commandTimeout;
        }

        private void buildConnection()
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout);
                _connection.Open();
            }
        }

        private async Task buildConnectionAsync(CancellationToken token)
        {
            if (_connection == null)
            {
                _connection = new TransactionState(_factory, _mode, _isolationLevel, _commandTimeout);
                await _connection.OpenAsync(token).ConfigureAwait(false);
            }
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public int RequestCount { get; private set; }

        public void Commit()
        {
            buildConnection();

            _connection.Commit();
        }

        public async Task CommitAsync(CancellationToken token)
        {
            await buildConnectionAsync(token).ConfigureAwait(false);

            await _connection.CommitAsync(token).ConfigureAwait(false);
        }

        public void Rollback()
        {
            if (_connection == null) return;

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
        }

        public async Task RollbackAsync(CancellationToken token)
        {
            if (_connection == null) return;

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
            catch (NpgsqlException e) when (e.Message.IndexOf(EventContracts.UnexpectedMaxEventIdForStream, StringComparison.Ordinal) > -1)
            {
                Logger.LogFailure(cmd, e);
                throw new EventStreamUnexpectedMaxEventIdException(e);
            }
            catch (Exception e)
            {
                Logger.LogFailure(cmd, e);
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
                Logger.LogFailure(cmd, e);
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
                Logger.LogFailure(cmd, e);
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
                Logger.LogFailure(cmd, e);
                throw;
            }
        }

        public async Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token);

            RequestCount++;

            var cmd = _connection.CreateCommand();

            try
            {
                await action(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                Logger.LogFailure(cmd, e);
                throw;
            }
        }

        public async Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token);

            RequestCount++;

            _connection.Apply(cmd);

            try
            {
                await action(cmd, token).ConfigureAwait(false);
                Logger.LogSuccess(cmd);
            }
            catch (Exception e)
            {
                Logger.LogFailure(cmd, e);
                throw;
            }
        }

        public async Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token);

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
                Logger.LogFailure(cmd, e);
                throw;
            }
        }

        public async Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            await buildConnectionAsync(token);

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
                Logger.LogFailure(cmd, e);
                throw;
            }
        }



        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}