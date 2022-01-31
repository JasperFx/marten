#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions
{
    internal class MartenControlledConnectionTransaction: IConnectionLifetime
    {
        protected readonly SessionOptions _options;


        public MartenControlledConnectionTransaction(SessionOptions options)
        {
            _options = options;
        }

        public int CommandTimeout => _options.Timeout ?? Connection?.CommandTimeout ?? 30;

        public async ValueTask DisposeAsync()
        {
            if (Transaction != null)
            {
                await Transaction.DisposeAsync().ConfigureAwait(false);
            }

            if (Connection != null)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }

        }

        public void Dispose()
        {
            Transaction?.SafeDispose();
            Connection?.SafeDispose();
        }

        public virtual void Apply(NpgsqlCommand command)
        {
            BeginTransaction();

            command.Connection = Connection;
            command.Transaction = Transaction;
            command.CommandTimeout = CommandTimeout;
        }

        public virtual void BeginTransaction()
        {
            if (Connection == null)
            {
#pragma warning disable CS8602
                Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
                Connection.Open();

            }

            if (Transaction == null)
            {
                Transaction = Connection.BeginTransaction(_options.IsolationLevel);
            }
        }

        // TODO -- this should be ValueTask
        public virtual async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);

            command.Connection = Connection;
            command.Transaction = Transaction;
            command.CommandTimeout = CommandTimeout;
        }

        public virtual async ValueTask BeginTransactionAsync(CancellationToken token)
        {
            if (Connection == null)
            {
#pragma warning disable CS8602
                Connection = _options.Tenant.Database.CreateConnection();
#pragma warning restore CS8602
                await Connection.OpenAsync(token).ConfigureAwait(false);

            }

#if NET5_0_OR_GREATER
            Transaction ??= await Connection
                .BeginTransactionAsync(_options.IsolationLevel, token).ConfigureAwait(false);
            #else
            Transaction ??= Connection.BeginTransaction(_options.IsolationLevel);
#endif
        }

        public void Commit()
        {
            if (Transaction == null)
                throw new InvalidOperationException("Trying to commit a transaction that was never started");
            Transaction.Commit();
            Transaction.Dispose();
            Transaction = null;

            Connection?.Close();
            Connection = null;
        }

        public async Task CommitAsync(CancellationToken token)
        {
            if (Transaction == null)
                throw new InvalidOperationException("Trying to commit a transaction that was never started");

            await Transaction.CommitAsync(token).ConfigureAwait(false);
            await Transaction.DisposeAsync().ConfigureAwait(false);
            Transaction = null;

            if (Connection != null)
            {
                await Connection.CloseAsync().ConfigureAwait(false);
                await Connection.DisposeAsync().ConfigureAwait(false);
            }

            Connection = null;
        }

        public void Rollback()
        {
            if (Transaction != null)
            {
                Transaction.Rollback();
                Transaction.Dispose();
                Transaction = null;

                Connection?.Close();
                Connection?.Dispose();
                Connection = null;
            }
        }

        public async Task RollbackAsync(CancellationToken token)
        {
            if (Transaction != null)
            {
                await Transaction.RollbackAsync(token).ConfigureAwait(false);
                await Transaction.DisposeAsync().ConfigureAwait(false);
                Transaction = null;

                if (Connection != null)
                {
                    await Connection.CloseAsync().ConfigureAwait(false);
                    await Connection.DisposeAsync().ConfigureAwait(false);
                }

                Connection = null;
            }
        }

        public NpgsqlConnection? Connection { get; protected set; }
        public NpgsqlTransaction? Transaction { get; protected set; }


    }
}
