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
        private readonly Lazy<TransactionState> _connection; 

        public ManagedConnection(IConnectionFactory factory) : this (factory, CommandRunnerMode.AutoCommit)
        {
        }

        // 30 is NpgsqlCommand.DefaultTimeout - ok to burn it to the call site?
        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int commandTimeout = 30)
        {
            _connection = new Lazy<TransactionState>(() => new TransactionState(factory, mode, isolationLevel, commandTimeout));
        }

        public IMartenSessionLogger Logger { get; set; } = new NulloMartenLogger();

        public int RequestCount { get; private set; }

        public void Commit()
        {
            _connection.Value.Commit();
        }

        public void Rollback()
        {
            _connection.Value.Rollback();
        }

        public NpgsqlConnection Connection => _connection.Value.Connection;

        public void Execute(NpgsqlCommand cmd, Action<NpgsqlCommand> action = null)
        {
            RequestCount++;

            if (action == null)
            {
                action = c => c.ExecuteNonQuery();
            }

            _connection.Value.Apply(cmd);
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
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
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
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
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
            RequestCount++;

            _connection.Value.Apply(cmd);
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
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();

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
            RequestCount++;

            _connection.Value.Apply(cmd);

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
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();

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
            RequestCount++;

            _connection.Value.Apply(cmd);

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
            if (_connection.IsValueCreated)
            {
                _connection.Value.SafeDispose();
            }
        }
    }
}