using System;
using System.Data;
using Baseline;
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
            Connection.Open();
            BeginTransaction();
        }

        private void BeginTransaction()
        {
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
            cmd.Transaction = Transaction;
            cmd.CommandTimeout = _commandTimeout;
        }

        public NpgsqlTransaction Transaction { get; private set; }

        public NpgsqlConnection Connection { get; }

        public void Commit()
        {
            Transaction.Commit();
            BeginTransaction();
        }

        public void Rollback()
        {
            Transaction.Rollback();
            BeginTransaction();
        }

        public void Dispose()
        {
            Transaction?.SafeDispose();
            Connection.SafeDispose();
        }

        public NpgsqlCommand CreateCommand()
        {
            var cmd = Connection.CreateCommand();
            cmd.Transaction = Transaction;

            return cmd;
        }
    }
}