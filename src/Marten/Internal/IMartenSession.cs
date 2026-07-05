#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Events;
using Npgsql;

namespace Marten.Internal;

public interface IMartenSession: IDisposable, IAsyncDisposable, IStorageSession
{
    IEventStorage EventStorage();

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);
}
