#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Weasel.Core.Operations;
using Weasel.Core.Operations.DirtyTracking;
using Weasel.Core.Serialization;

namespace Marten.Internal;

public interface IMartenSession: IDisposable, IAsyncDisposable, IStorageSession
{
    Dictionary<Type, object> ItemMap { get; }

    // TODO -- try to encapsulate this
    IMartenDatabase Database { get; }

    VersionTracker Versions { get; }

    StoreOptions Options { get; }

    IList<IChangeTracker> ChangeTrackers { get; }

    /// <summary>
    ///     Override whether or not this session honors optimistic concurrency checks
    /// </summary>
    ConcurrencyChecks Concurrency { get; }

    IDocumentStorage StorageFor(Type documentType);


    void MarkAsAddedForStorage(object id, object document);

    void MarkAsDocumentLoaded(object id, object document);
    IDocumentStorage<T> StorageFor<T>() where T : notnull;

    IEventStorage EventStorage();

    string NextTempTableName();

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    DbDataReader ExecuteReader(NpgsqlCommand command);

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);
}
