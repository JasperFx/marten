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
    // #4819: closed-shape storage sees the db-neutral IStorageSerializer via IStorageSession;
    // Marten's own code keeps the full ISerializer here (ValueCasting, ToJsonWithTypes, the
    // buffer-writer overloads, etc.). Every Marten session returns an ISerializer, which satisfies
    // both since ISerializer : IStorageSerializer.
    new ISerializer Serializer { get; }

    IEventStorage EventStorage();

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);
}
