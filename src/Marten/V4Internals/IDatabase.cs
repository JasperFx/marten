using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Marten.V4Internals
{
    public interface IDatabase : IDisposable
    {
        int RequestCount { get; }
        NpgsqlConnection Connection { get; }

        DbDataReader ExecuteReader(NpgsqlCommand command);
        Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token);
    }



}
