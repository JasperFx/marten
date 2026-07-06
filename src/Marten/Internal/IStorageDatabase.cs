#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core.Sequences;

namespace Marten.Internal;

/// <summary>
///     Database-neutral database accessor the closed-shape storage runtime targets instead of
///     <see cref="Marten.Storage.IMartenDatabase"/> (#4827, part of the #4821 extraction epic).
///     Exposes only the two db-neutral concerns the movable storage code consumes off the session's
///     database: the <see cref="IProviderGraph"/> for document-provider lookup, and the
///     <see cref="ISequenceSource"/> Hi-Lo/sequence seam (already agnostic from #4811) used by the
///     Weasel.Core.Identity <c>AssignIfMissing</c> strategies.
/// </summary>
/// <remarks>
///     Deliberately does <em>not</em> surface a connection factory: the projection-safe read path
///     (<see cref="Marten.Internal.ClosedShape.ClosedShapeProjectionLoader"/>) still builds and runs
///     Npgsql-typed commands, so it keeps taking the Npgsql-typed
///     <see cref="Marten.Storage.IMartenDatabase"/> until the command/connection surface is made
///     agnostic in a later story. Marten's <see cref="Marten.Storage.IMartenDatabase"/> implements
///     this interface — its <c>Providers</c> and <c>ISequenceSource</c> members already satisfy it.
/// </remarks>
public interface IStorageDatabase: ISequenceSource
{
    IProviderGraph Providers { get; }

    /// <summary>
    ///     Open a new database-neutral connection. The projection-safe closed-shape read path
    ///     (<see cref="Marten.Internal.ClosedShape.ClosedShapeProjectionLoader{TDoc,TId}"/>) uses this to
    ///     run its <see cref="DbCommand"/> off the session, so it targets the neutral
    ///     <see cref="DbConnection"/> here instead of the Npgsql-typed
    ///     <see cref="Marten.Storage.IMartenDatabase"/> connection factory. (Distinct name from the
    ///     Npgsql-typed <c>CreateConnection</c> to avoid a same-signature return-type clash.)
    /// </summary>
    DbConnection CreateStorageConnection();

    /// <summary>
    ///     Execute a single SQL statement against a fresh connection. Used by the closed-shape
    ///     <c>TruncateDocumentStorageAsync</c> path so it no longer needs the Npgsql-typed database.
    /// </summary>
    Task RunSqlAsync(string sql, CancellationToken ct = default);
}
