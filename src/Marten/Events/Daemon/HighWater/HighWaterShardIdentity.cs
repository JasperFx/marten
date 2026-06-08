#nullable enable
using JasperFx.Events.Projections;

namespace Marten.Events.Daemon.HighWater;

/// <summary>
/// Canonical producer for high-water progression-row identities — the single source of truth
/// for the <c>mt_event_progression.name</c> values that the high-water machinery writes and
/// reads. Routes all per-tenant identity construction through this helper so any future grammar
/// change (separator, version prefix, escape rule) only needs to land in one place rather than
/// being smeared across SQL string concatenations.
///
/// <para>
/// The high-water grammar is intentionally asymmetric to the rest of <see cref="ShardName"/>:
/// JasperFx's <c>ShardName</c> constructor special-cases the HighWaterMark name and collapses
/// its <c>Identity</c> to the literal constant (dropping any tenant slot). Marten layers
/// per-tenant high-water tracking on top by writing one row per tenant under the convention
/// <c>"{HighWaterMark}:{tenantId}"</c>. See the comment in <c>EventProgressionTable</c> for the
/// surrounding storage-side context — both sides need to agree on this shape exactly, and any
/// drift (a missing colon, a different separator) silently desyncs the writers from the
/// readers because the SQL is keyed by string equality on <c>name</c>.
/// </para>
/// </summary>
/// <seealso cref="ShardState.HighWaterMark"/>
internal static class HighWaterShardIdentity
{
    /// <summary>
    /// Identity for the store-global high-water progression row -- the only row that exists
    /// when per-tenant partitioning is off. Equal to <see cref="ShardState.HighWaterMark"/>.
    /// </summary>
    public const string StoreGlobal = ShardState.HighWaterMark;

    /// <summary>
    /// Prefix prepended to a tenant id to form the per-tenant high-water identity. Useful when
    /// the per-tenant name is built inside SQL (e.g. by concatenating with a column value);
    /// for cases where the full identity is built in C#, prefer <see cref="PerTenant"/>.
    /// </summary>
    public const string PerTenantPrefix = ShardState.HighWaterMark + ":";

    /// <summary>
    /// Full identity for a tenant's high-water progression row, under the per-tenant
    /// partitioning naming convention.
    /// </summary>
    public static string PerTenant(string tenantId) => PerTenantPrefix + tenantId;
}
