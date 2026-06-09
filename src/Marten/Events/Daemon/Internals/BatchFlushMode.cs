#nullable enable
using Weasel.Core;

namespace Marten.Events.Daemon.Internals;

/// <summary>
/// Classifies a <see cref="ProjectionUpdateBatch"/> by what kind of write traffic it has
/// accumulated. The classifier is the seam where the BulkWriter (binary <c>COPY</c>) rebuild
/// flush path being added in #4685 plugs in: rebuild post-<c>TRUNCATE</c> is INSERT-only and
/// safe to dispatch through <c>BeginBinaryImport</c>; continuous catch-up mixes updates / upserts
/// / patches / deletes and must keep the per-row UPSERT path.
/// </summary>
public enum BatchFlushMode
{
    /// <summary>
    /// Default. The batch holds (or could hold) operations beyond plain inserts -- updates,
    /// upserts, patches, deletes, ad-hoc SQL. The existing per-row UPSERT flush is the only
    /// safe choice; the BulkWriter path is skipped.
    /// </summary>
    Mixed,

    /// <summary>
    /// Every operation accumulated so far carries <c>OperationRole.Insert</c>. Eligible for
    /// the BulkWriter (binary <c>COPY</c>) flush path being added in subsequent PRs of #4685.
    /// A rebuild that <c>TRUNCATE</c>d its target table before replaying events is the
    /// canonical producer of this mode.
    /// </summary>
    InsertOnly
}

/// <summary>
/// Pure classifier for <see cref="BatchFlushMode"/>. Extracted so the incremental update done
/// inside <see cref="ProjectionUpdateBatch"/> is unit-testable without standing up a full
/// session / database, and so the transition rule lives in one place that subsequent PRs in
/// the #4685 series can dispatch against.
/// </summary>
internal static class BatchFlushModeClassifier
{
    /// <summary>
    /// Initial mode for an empty batch. <see cref="BatchFlushMode.InsertOnly"/> -- a batch
    /// that never sees any operation is trivially insert-only (the BulkWriter path is a
    /// no-op on zero rows), and demoting only on a non-Insert arrival keeps the transition
    /// monotonic.
    /// </summary>
    public const BatchFlushMode Initial = BatchFlushMode.InsertOnly;

    /// <summary>
    /// Update <paramref name="current"/> with the arrival of a new operation whose role is
    /// <paramref name="role"/>. Once a batch demotes to <see cref="BatchFlushMode.Mixed"/> it
    /// stays there for the rest of its lifetime; the transition is monotonic so the caller
    /// can short-circuit further classification work once it's seen a non-Insert.
    /// </summary>
    public static BatchFlushMode WithOperation(BatchFlushMode current, OperationRole role)
    {
        if (current == BatchFlushMode.Mixed) return BatchFlushMode.Mixed;
        return role == OperationRole.Insert ? BatchFlushMode.InsertOnly : BatchFlushMode.Mixed;
    }
}
