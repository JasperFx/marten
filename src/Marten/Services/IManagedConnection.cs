using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
#nullable enable
namespace Marten.Services
{
    public interface IManagedConnection: IDisposable, IAsyncDisposable
    {
        int Execute(NpgsqlCommand cmd);

        Task<int> ExecuteAsync(NpgsqlCommand cmd, CancellationToken token = default);

        DbDataReader ExecuteReader(NpgsqlCommand command);
        Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);

        void Commit();

        void Rollback();

        NpgsqlConnection Connection { get; }

        int RequestCount { get; }
        IMartenSessionLogger Logger { get; set; }

        void BeginTransaction();

        bool InTransaction();

        Task BeginTransactionAsync(CancellationToken token);

        Task CommitAsync(CancellationToken token);

        Task RollbackAsync(CancellationToken token);

        void BeginSession();



    }
}
