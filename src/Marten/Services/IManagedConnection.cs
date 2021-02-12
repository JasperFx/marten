using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;
using Npgsql;

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


    internal static class ManagedConnectionExtensions
    {
        internal static T LoadOne<T>(this IManagedConnection connection, NpgsqlCommand command, ISelector<T> selector)
        {
            using (var reader = connection.ExecuteReader(command))
            {
                if (!reader.Read()) return default(T);

                return selector.Resolve(reader);
            }
        }

        internal static async Task<T> LoadOneAsync<T>(this IManagedConnection connection, NpgsqlCommand command, ISelector<T> selector, CancellationToken token)
        {
            using (var reader = await connection.ExecuteReaderAsync(command, token))
            {
                if (!(await reader.ReadAsync(token))) return default(T);

                return await selector.ResolveAsync(reader, token);
            }
        }
    }

}
