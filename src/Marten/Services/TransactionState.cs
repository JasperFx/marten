using System;
using System.Data;
using Baseline;
using Npgsql;

namespace Marten.Services
{
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
}