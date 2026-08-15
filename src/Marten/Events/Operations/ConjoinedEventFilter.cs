#nullable enable
using JasperFx;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// #5234: the tenant predicate every operation that keys an <c>mt_events</c> rewrite on
/// <c>seq_id</c> has to carry.
///
/// <para>
/// Under <see cref="EventGraph.UseTenantPartitionedEvents" /> each tenant draws from its own
/// <c>mt_events_sequence_{suffix}</c>, so <c>seq_id</c> is NOT unique across tenants —
/// <c>seq_id = 1</c> exists in every tenant's partition. A <c>where seq_id = ?</c> with no tenant
/// predicate therefore rewrites, or deletes, the same-numbered event in every other tenant too.
/// The read side of masking and compaction is correctly tenant-scoped, so the sequences collected
/// belong to the calling tenant; only the write escaped.
/// </para>
///
/// <para>
/// Gated on <see cref="TenancyStyle.Conjoined" /> rather than on
/// <c>UseTenantPartitionedEvents</c>, matching <see cref="SetEventTagsHstoreOperation" />, which is
/// the one operation of this shape that already guarded itself. Conjoined-without-partitioning
/// keeps a single global sequence, so there the predicate is a no-op that filters nothing — but one
/// shape across every call site is worth more than the narrower condition, and it means a future
/// mode that makes <c>seq_id</c> ambiguous is covered by construction.
/// </para>
/// </summary>
internal static class ConjoinedEventFilter
{
    /// <summary>
    ///     Appends <c>and tenant_id = ?</c> when the event store is conjoined. Call immediately after
    ///     writing the <c>seq_id</c> predicate.
    /// </summary>
    public static void AppendConjoinedTenantFilter(this ICommandBuilder builder, IStorageSession session)
    {
        if (((IMartenSession)session).Options.Events.TenancyStyle != TenancyStyle.Conjoined)
        {
            return;
        }

        builder.Append(" and tenant_id = ");
        builder.AppendParameter(session.TenantId);
    }
}
