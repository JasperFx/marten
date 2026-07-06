#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Internal.DirtyTracking;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal;

/// <summary>
///     Database-neutral operation/session context that Marten's closed-shape storage,
///     operation, and selector code targets instead of <see cref="IMartenSession"/>.
///     This is the gating seam (#4810) for extracting the closed-shape storage runtime
///     into a shared package so alternate dialects (e.g. Polecat/SQL Server) can reuse it.
/// </summary>
/// <remarks>
///     Deliberately exposes only the subset of members the closed-shape document + event
///     storage code actually consumes. The unit-of-work metadata seam is the shared
///     <see cref="IMetadataContext"/> (TenantId / correlation / causation / user name /
///     headers) — the JasperFx interface Marten's session already implements.
///     Members that are still Marten-typed (<see cref="ISerializer"/>,
///     <see cref="IMartenDatabase"/>, <see cref="StoreOptions"/>) are intentionally left as
///     the Marten types for now — a serializer seam and a database/sequence seam are tracked
///     as separate W2 tasks. This interface only makes the code <em>targetable</em>; the
///     physical move to a shared package is a later step.
/// </remarks>
public interface IStorageSession: IMetadataContext
{
    IStorageSerializer Serializer { get; }

    IMartenDatabase Database { get; }

    IVersionTracker Versions { get; }

    IList<IChangeTracker> ChangeTrackers { get; }

    Dictionary<Type, object> ItemMap { get; }

    /// <summary>
    ///     Override whether or not this session honors optimistic concurrency checks
    /// </summary>
    ConcurrencyChecks Concurrency { get; }

    IDocumentStorage StorageFor(Type documentType);

    IDocumentStorage<T> StorageFor<T>() where T : notnull;

    void MarkAsAddedForStorage(object id, object document);

    void MarkAsDocumentLoaded(object id, object document);

    /// <summary>
    ///     Execute a single command against this session's connection and return the results.
    ///     Db-neutral execution seam (#4810): the closed-shape read path (document LoadAsync /
    ///     LoadManyAsync) targets <see cref="System.Data.Common.DbCommand"/> here instead of the
    ///     Npgsql-typed <see cref="IMartenSession.ExecuteReaderAsync(Npgsql.NpgsqlCommand, CancellationToken)"/>.
    ///     Marten's session implements this over its Npgsql connection lifetime; this is the
    ///     interim seam until a Weasel.Core execution abstraction lands.
    /// </summary>
    Task<DbDataReader> ExecuteReaderAsync(DbCommand command, CancellationToken token = default);

    /// <summary>
    ///     Generates a unique temporary-table / CTE name scoped to this session. Used by the LINQ
    ///     statement compiler for include queries and chained sub-selects (#4810). A session-scoped
    ///     concern any dialect performing include/CTE queries needs.
    /// </summary>
    string NextTempTableName();
}
