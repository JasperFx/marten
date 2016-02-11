using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Npgsql;

namespace Marten.Services
{
    public enum CommandRunnerMode
    {
        Transactional,
        ReadOnly
    }

    public class TransactionState : IDisposable
    {
        private readonly IsolationLevel _isolationLevel;


        public TransactionState(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel)
        {
            _isolationLevel = isolationLevel;
            Connection = factory.Create();
            Connection.Open();
            if (mode == CommandRunnerMode.Transactional)
            {
                Transaction = Connection.BeginTransaction(isolationLevel);
            }
        }

        public void Apply(NpgsqlCommand cmd)
        {
            cmd.Connection = Connection;
            cmd.Transaction = Transaction;
        }

        public NpgsqlTransaction Transaction { get; private set; }

        public NpgsqlConnection Connection { get; }

        public void Commit()
        {
            Transaction.Commit();
            Transaction = Connection.BeginTransaction(_isolationLevel);
        }

        public void Rollback()
        {
            Transaction.Rollback();
            Transaction = Connection.BeginTransaction(_isolationLevel);
        }

        public void Dispose()
        {
            Connection.Close();
            Connection.SafeDispose();
        }

        public NpgsqlCommand CreateCommand()
        {
            var cmd = Connection.CreateCommand();
            cmd.Transaction = Transaction;

            return cmd;
        }
    }

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
            if (action == null)
            {
                action = c => c.ExecuteNonQuery();
            }

            _connection.Value.Apply(cmd);
            action(cmd);
        }

        public void Execute(Action<NpgsqlCommand> action)
        {
            var cmd = _connection.Value.CreateCommand();
            action(cmd);
        }

        public T Execute<T>(Func<NpgsqlCommand, T> func)
        {
            var cmd = _connection.Value.CreateCommand();
            return func(cmd);
        }

        public T Execute<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, T> func)
        {
            _connection.Value.Apply(cmd);
            return func(cmd);
        }

        public async Task ExecuteAsync(Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            var cmd = _connection.Value.CreateCommand();
            await action(cmd, token);
        }

        public async Task ExecuteAsync(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task> action, CancellationToken token = new CancellationToken())
        {
            _connection.Value.Apply(cmd);
            await action(cmd, token);
        }

        public async Task<T> ExecuteAsync<T>(Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
            var cmd = _connection.Value.CreateCommand();
            return await func(cmd, token);
        }

        public async Task<T> ExecuteAsync<T>(NpgsqlCommand cmd, Func<NpgsqlCommand, CancellationToken, Task<T>> func, CancellationToken token = new CancellationToken())
        {
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