using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.Services
{
    public interface IManagedConnection: IDisposable
    {
        int Execute(NpgsqlCommand cmd);

        Task<int> ExecuteAsync(NpgsqlCommand cmd, CancellationToken token = default);

        DbDataReader ExecuteReader(NpgsqlCommand command);
        Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);

        void Commit();

        void Rollback();

        NpgsqlConnection Connection { get; }

        int RequestCount { get; }

        void BeginTransaction();

        bool InTransaction();

        Task BeginTransactionAsync(CancellationToken token);

        Task CommitAsync(CancellationToken token);

        Task RollbackAsync(CancellationToken token);

        void BeginSession();



    }

}
