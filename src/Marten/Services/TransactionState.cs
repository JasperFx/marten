using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    public class TransactionState : IDisposable
    {
        private readonly CommandRunnerMode _mode;
        private readonly IsolationLevel _isolationLevel;
        private readonly int _commandTimeout;

        public TransactionState(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel, int commandTimeout)
        {
            _mode = mode;
            _isolationLevel = isolationLevel;
            this._commandTimeout = commandTimeout;
            Connection = factory.Create();
        }

        public bool IsOpen => Connection.State != ConnectionState.Closed;

        public void Open()
        {
            Connection.Open();
        }

        public Task OpenAsync(CancellationToken token)
        {
            return Connection.OpenAsync(token);
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public void BeginTransaction()
        {
            if (Transaction != null) return;

            if (_mode == CommandRunnerMode.Transactional || _mode == CommandRunnerMode.ReadOnly)
            {
                Transaction = Connection.BeginTransaction(_isolationLevel);
            }

            if (_mode == CommandRunnerMode.ReadOnly)
            {
                using (var cmd = new NpgsqlCommand("SET TRANSACTION READ ONLY;"))
                {
                    Apply(cmd);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void Apply(NpgsqlCommand cmd)
        {
            cmd.Connection = Connection;
            if (Transaction != null) cmd.Transaction = Transaction;
            cmd.CommandTimeout = _commandTimeout;
        }

        public NpgsqlTransaction Transaction { get; private set; }

        public NpgsqlConnection Connection { get; }
        
        public void Commit()
        {
            Transaction?.Commit();
            Transaction?.Dispose();
            Transaction = null;
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (Transaction != null)
            {
                await Transaction.CommitAsync(token).ConfigureAwait(false);

                Transaction.Dispose();
                Transaction = null;
            }
        }

        public void Rollback()
        {
            if (Transaction != null && !Transaction.IsCompleted)
            {
                try
                {
                    Transaction?.Rollback();
                    Transaction?.Dispose();
                    Transaction = null;
                }
                catch (Exception e)
                {
                    throw new RollbackException(e);
                }
            }
        }

        public async Task RollbackAsync(CancellationToken token)
        {
            if (Transaction != null && !Transaction.IsCompleted)
            {
                try
                {
                    await Transaction.RollbackAsync(token).ConfigureAwait(false);
                    Transaction.Dispose();
                    Transaction = null;
                }
                catch (Exception e)
                {
                    throw new RollbackException(e);
                }
            }
        }

        public void Dispose()
        {
            Transaction?.Dispose();
            Transaction = null;


            Connection.Close();
            Connection.Dispose();
        }

        public NpgsqlCommand CreateCommand()
        {
            var cmd = Connection.CreateCommand();
            if (Transaction != null) cmd.Transaction = Transaction;

            return cmd;
        }
    }

    public class RollbackException : Exception
    {
        public RollbackException(Exception innerException) : base("Failed while trying to rollback an exception", innerException)
        {
        }
    }
}