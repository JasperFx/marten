using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Npgsql;

namespace Marten.Internal.Sessions
{
    internal class AmbientTransactionLifetime: IConnectionLifetime
    {
        private readonly SessionOptions _options;

        public AmbientTransactionLifetime(SessionOptions options)
        {
            _options = options;
        }

        public NpgsqlConnection? Connection { get; private set; }

        public int CommandTimeout => _options.Timeout ?? Connection?.CommandTimeout ?? 30;


        public ValueTask DisposeAsync()
        {
            if (Connection != null)
            {
                return Connection.DisposeAsync();
            }

            return new ValueTask();
        }

        public void Dispose()
        {
            Connection?.Close();
            Connection?.Dispose();
        }

        public void Apply(NpgsqlCommand command)
        {
            command.Connection = Connection;
            command.CommandTimeout = CommandTimeout;
        }

        public async Task ApplyAsync(NpgsqlCommand command, CancellationToken token)
        {
            await BeginTransactionAsync(token).ConfigureAwait(false);
            command.Connection = Connection;
            command.CommandTimeout = CommandTimeout;
        }

        public void Commit()
        {
            // Nothing
        }

        public Task CommitAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public void Rollback()
        {
            // Nothing
        }

        public Task RollbackAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public void BeginTransaction()
        {
            if (Connection == null)
            {
                Connection = _options.Tenant.Storage.CreateConnection();
                Connection.Open();
                Connection.EnlistTransaction(_options.DotNetTransaction);
            }
        }

        public async ValueTask BeginTransactionAsync(CancellationToken token)
        {
            if (Connection == null)
            {
                Connection = _options.Tenant.Storage.CreateConnection();
                await Connection.OpenAsync(token).ConfigureAwait(false);
                Connection.EnlistTransaction(_options.DotNetTransaction);
            }
        }
    }
}
