#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events.Projections;

namespace Marten.Events.Daemon;

/// <summary>
///     The <c>mt_event_progression</c> rows that a delete of events has to wait for: one per registered async
///     projection shard and subscription. Shared by stream compaction (#6050) and the cold move
///     (<c>MarkStreamsCold</c>), so the two lag guards apply one rule and can't drift apart.
/// </summary>
/// <remarks>
///     The rows are named the way the daemon writes continuous progress: per tenant (<c>{shard}:{tenant}</c>)
///     when the store distributes its agents per tenant, which is <c>UseTenantPartitionedEvents</c>, and
///     store-global otherwise. A shard with no row has never run, and counts as behind.
/// </remarks>
internal static class AsyncShardProgressRows
{
    /// <param name="exemptProjections">Names of projections whose shards are not checked.</param>
    public static string[] For(EventGraph events, string tenantId, IReadOnlySet<string>? exemptProjections = null)
    {
        var perTenant = events.UseTenantPartitionedEvents;
        return events.Options.Projections.AllShards()
            .Where(x => exemptProjections == null || !exemptProjections.Contains(x.Name.Name))
            .Select(x => perTenant
                ? ShardName.Compose(x.Name.Name, x.Name.ShardKey, tenantId, x.Name.Version).Identity
                : x.Name.Identity)
            .Distinct()
            .ToArray();
    }
}
