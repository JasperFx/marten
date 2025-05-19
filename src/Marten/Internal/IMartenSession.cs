#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Events;
using Marten.Internal.DirtyTracking;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal;

public interface IMartenSession: IDisposable, IAsyncDisposable, IMetadataContext
{
    ISerializer Serializer { get; }
    Dictionary<Type, object> ItemMap { get; }
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
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);
}
