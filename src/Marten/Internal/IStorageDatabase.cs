#nullable enable
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
}
