using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Npgsql;

namespace Marten.Services
{
    public class ManagedConnection : IManagedConnection
    {
        private readonly Lazy<TransactionState> _connection; 

        public ManagedConnection(IConnectionFactory factory) : this (factory, CommandRunnerMode.ReadOnly)
        {
        }

        public ManagedConnection(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel = IsolationLevel.ReadUncommitted)
        {
            _connection = new Lazy<TransactionState>(() => new TransactionState(factory, mode, isolationLevel));
        }

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
            action(cmd);
        }

        public void Execute(Action<NpgsqlCommand> action)
        {
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
            action(cmd);
        }

        public T Execute<T>(Func<NpgsqlCommand, T> func)
        {
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
            return func(cmd);
        }

        public T Execute<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, T> func)
        {
            RequestCount++;

            _connection.Value.Apply(cmd);
            return func(cmd);
        }

        public async Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
            await action(cmd, token);
        }

        public async Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            RequestCount++;

            _connection.Value.Apply(cmd);
            await action(cmd, token);
        }

        public async Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            RequestCount++;

            var cmd = _connection.Value.CreateCommand();
            return await func(cmd, token);
        }

        public async Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            RequestCount++;

            _connection.Value.Apply(cmd);
            return await func(cmd, token);
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