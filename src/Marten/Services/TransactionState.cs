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

        public TransactionState(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel)
        {
            _mode = mode;
            _isolationLevel = isolationLevel;
            Connection = factory.Create();
            Connection.Open();
            BeginTransaction();
        }

        private void BeginTransaction()
        {
            if (_mode == CommandRunnerMode.Transactional)
            {
                Transaction = Connection.BeginTransaction(_isolationLevel);
            }
            //TODO come back to this, but LOTS of tests need to be fixed for this to work
            //if (_mode == CommandRunnerMode.ReadOnly)
            //{
            //    using (var cmd = new NpgsqlCommand("SET TRANSACTION READ ONLY;"))
            //    {
            //        Apply(cmd);
            //        cmd.ExecuteNonQuery();
            //    }
            //}
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