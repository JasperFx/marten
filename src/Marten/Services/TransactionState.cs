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
        private readonly bool _ownsConnection;

        public TransactionState(CommandRunnerMode mode, IsolationLevel isolationLevel, int commandTimeout, NpgsqlConnection connection, bool ownsConnection, NpgsqlTransaction transaction = null)
        {
            _mode = mode;
            _isolationLevel = isolationLevel;
            _commandTimeout = commandTimeout;
            _ownsConnection = ownsConnection;
            Transaction = transaction;
            Connection = connection;
        }

        public TransactionState(IConnectionFactory factory, CommandRunnerMode mode, IsolationLevel isolationLevel, int commandTimeout, bool ownsConnection)
        {
            _mode = mode;
            _isolationLevel = isolationLevel;
            this._commandTimeout = commandTimeout;
            _ownsConnection = ownsConnection;
            Connection = factory.Create();
        }

        public bool IsOpen => Connection.State != ConnectionState.Closed;

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            Connection.Open();
        }

        public Task OpenAsync(CancellationToken token)
        {
            if (IsOpen)
            {
                return Task.CompletedTask;
            }
            return Connection.OpenAsync(token);
        }

        public IMartenSessionLogger Logger { get; set; } = NulloMartenLogger.Flyweight;

        public void BeginTransaction()
        {
            if (Transaction != null || _mode == CommandRunnerMode.External) return;

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
            if (_mode != CommandRunnerMode.External)
            {
                Transaction?.Commit();
                Transaction?.Dispose();
                Transaction = null;
            }

            if (_ownsConnection)
            {
                Connection.Close();
            }
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (Transaction != null && _mode != CommandRunnerMode.External)
            {
                await Transaction.CommitAsync(token).ConfigureAwait(false);

                Transaction.Dispose();
                Transaction = null;
            }

            if (_ownsConnection)
            {
                Connection.Close();
            }
        }

        public void Rollback()
        {
            if (Transaction != null && !Transaction.IsCompleted && _mode != CommandRunnerMode.External)
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
                finally
                {
                    Connection.Close();
                }
            }
        }

        public async Task RollbackAsync(CancellationToken token)
        {
            if (Transaction != null && !Transaction.IsCompleted && _mode != CommandRunnerMode.External)
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
                finally
                {
                    Connection.Close();
                }
            }
        }

        public void Dispose()
        {
            if (_mode != CommandRunnerMode.External)
            {
                Transaction?.Dispose();
                Transaction = null;
            }

            if (_ownsConnection)
            {
                Connection.Close();
                Connection.Dispose();
            }
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