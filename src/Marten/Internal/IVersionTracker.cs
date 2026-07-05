#nullable enable
using System;
using System.Collections.Generic;

namespace Marten.Internal;

/// <summary>
///     Agnostic optimistic-concurrency version/revision tracker seam consumed by the closed-shape
///     storage runtime (#4825, part of the #4821 extraction epic). Every member is already
///     database-neutral, so this is simply the existing <see cref="VersionTracker"/> surface lifted
///     to an interface the storage code can target instead of the concrete Marten type.
/// </summary>
public interface IVersionTracker
{
    Dictionary<TId, long> RevisionsFor<TDoc, TId>() where TId : notnull;

    Dictionary<TId, Guid> ForType<TDoc, TId>() where TId : notnull;

    Guid? VersionFor<TDoc, TId>(TId id) where TId : notnull;

    long? RevisionFor<TDoc, TId>(TId id) where TId : notnull;

    void StoreVersion<TDoc, TId>(TId id, Guid guid) where TId : notnull;

    void StoreRevision<TDoc, TId>(TId id, long revision) where TId : notnull;

    void ClearVersion<TDoc, TId>(TId id) where TId : notnull;

    void ClearRevision<TDoc, TId>(TId id) where TId : notnull;
}
